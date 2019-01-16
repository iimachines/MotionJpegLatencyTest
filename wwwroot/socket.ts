interface WithFrameId {
    frameId: number;
}

interface SegmentHeader extends WithFrameId {
    frameTime: number;
    bandWidth: number;
    frameRate: number;
    renderDuration: number;
    transmitDuration: number;
    compressDuration: number;
    frameDuration: number;
    segmentX: number;
    segmentY: number;
}

interface DecodedSegment {
    header: SegmentHeader;
    image: ImageBitmap;
}

interface JitterBuffer {
    frameId: number;
    isFailed: boolean;
    pending: number;
    segments: DecodedSegment[];
}

interface EncodedEntry {
    frameId: number;
    bytes: Uint8Array;
    header: SegmentHeader;
}

namespace SocketThread {

    const worker: Worker = self as any;
    const segmentCount = 4;

    let socket: (WebSocket & { bufferedAmount?: number }) | null = null;

    type ImageDecoder = Worker;

    function postResponse(action: string, payload: any, transfer?: Transferable[]) {
        worker.postMessage({ action, payload }, transfer);
    }

    function createImageDecoder(id: number): ImageDecoder {
        if (typeof createImageBitmap === "function") {
            return new Worker("decoder.js");
        }

        // createImageBitmap not available (Edge, IE, older browsers)
        throw new Error("createImageBitmap not supported by your browser");
    }

    function isDecoderReady(d: ImageDecoder) {
        return !d.onmessage;
    }

    const imageDecodingWorkers = [...Array(segmentCount)].map(createImageDecoder);

    const jitterBuffers: Record<number, JitterBuffer> = {};

    function getJitterBuffer(frameId: number) {
        return jitterBuffers[frameId] || (jitterBuffers[frameId] = { frameId, isFailed: false, pending: segmentCount, segments: [] });
    }

    function onSegmentReceived(frameId: number) {
        const jb = getJitterBuffer(frameId);
        jb.pending -= 1;
        if (jb.pending === 0) {
            delete jitterBuffers[jb.frameId];
        }
        return jb;
    }

    function decodeNext(decoder: ImageDecoder) {
        decoder.onmessage = null;
        decoder.onerror = null;
        // const entry = decodingQueue.pop();

        // if (entry) {
        //     loadImageWithWorker(entry);
        // }
    }

    function loadImageWithWorker(entry: EncodedEntry): Promise<JitterBuffer> {
        const decoder = imageDecodingWorkers.filter(isDecoderReady)[0];

        return new Promise((resolve, reject) => {
            if (decoder) {
                decoder.onmessage = (e) => {
                    const { frameId, header } = entry;
                    decodeNext(decoder);
                    const jb = onSegmentReceived(frameId);
                    if (e.data.error) {
                        if (!jb.isFailed) {
                            jb.isFailed = true;
                            reject({ error: e.data.error, frameId });
                        }
                    } else {
                        const [image] = e.data as [ImageBitmap];
                        if (jb.isFailed) {
                            image.close();
                        } else {
                            jb.segments.push({ image, header });
                            if (jb.pending === 0) {
                                resolve(jb);
                            }
                        }
                    }
                };
                decoder.onerror = (e) => {
                    const { frameId } = entry;
                    decodeNext(decoder);
                    reject({ error: e.error, frameId });
                };

                const { frameId, bytes } = entry;
                decoder.postMessage([frameId, bytes.buffer, bytes.byteOffset, bytes.length], [bytes.buffer]);
            } else {
                const { frameId } = entry;
                // if (decodingQueue.length < imageDecodingWorkers.length) {
                //     decodingQueue.push(entry);
                // } else {
                    console.warn(`Decoding queue is full, rejecting frame ${frameId}`);
                    reject({ frameId, error: null });
                //}
            }
        });
    }

    function stopSocket() {
        if (socket) {
            socket.close();
            socket = null;
        }
    }

    function startSocket() {

        // Already started?
        if (socket)
            return;

        const scheme = location.protocol === "https:" ? "wss" : "ws";
        const port = location.port ? (":" + location.port) : "";
        const url = scheme + "://" + location.hostname + port + "/renderer";
        socket = new WebSocket(url);
        socket.binaryType = "arraybuffer";

        socket.onmessage = evt => {
            if (typeof evt.data === "string") {
                worker.postMessage(JSON.parse(evt.data));
            } else {
                // Received encoded image with stats header
                // See C# FrameHeader struct
                const headerSize = 10 * 8;

                const buffer: ArrayBuffer = evt.data;
                const bytes = new Uint8Array(buffer, headerSize);

                let i = 0;
                const dblView = new Float64Array(buffer, 0, headerSize);

                const header: SegmentHeader = {
                    frameId: dblView[i++] | 0,
                    frameTime: dblView[i++],
                    bandWidth: dblView[i++],
                    frameRate: dblView[i++],
                    renderDuration: dblView[i++],
                    transmitDuration: dblView[i++],
                    compressDuration: dblView[i++],
                    frameDuration: dblView[i++],
                    segmentX: dblView[i++] | 0,
                    segmentY: dblView[i++] | 0,
                }

                postResponse("onDecodeBegin", header);

                const { frameId } = header;

                loadImageWithWorker({frameId, bytes, header}).then(jitterBuffer => {
                    postResponse("onDecodeSuccess", jitterBuffer, jitterBuffer.segments.map(s => s.image));
                }, payload => {
                    postResponse("onDecodeFailure", payload);
                });
            }
        };
        socket.onopen = evt => {
            worker.postMessage({ action: "onopen" });
        }
        socket.onerror = evt => {
            worker.postMessage({ action: "onerror" });
        };
        socket.onclose = () => {
            worker.postMessage({ action: "onclose" });
        };
    }

    worker.onmessage = evt => {
        const { action, payload } = evt.data;

        switch (action) {
            case "START": {
                startSocket();
                break;
            }
            case "STOP": {
                stopSocket();
                break;
            }

            case "TICK": {
                const message = JSON.stringify(evt.data);

                if (socket && socket.readyState === socket.OPEN) {
                    if (socket.bufferedAmount > 1000) {
                        console.warn(`Server is too busy, not sending request, dropping ${message}`)
                        postResponse("onDecodeFailure", { ...payload, error: "server too busy" });
                    } else {
                        socket.send(message);
                        postResponse("onDecodeRequest", payload);
                    }
                } else {
                    console.error(`Cannot send to websocket, loosing ${message}`)
                }
                break;
            }

            default:
                console.error(`Socket worker received unknown request ${action}`);
                break;
        }
    }

    startSocket();
}
