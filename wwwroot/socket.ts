interface WithFrameId {
    frameId: number;
}

interface FrameHeader extends WithFrameId {
    frameTime: number;
    bandWidth: number;
    frameRate: number;
    renderDuration: number;
    transmitDuration: number;
    compressDuration: number;
    frameDuration: number;
}

namespace SocketThread {

    const worker: Worker = self as any;

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

    const imageDecodingWorkers = [...Array(3)].map(createImageDecoder);

    function loadImageWithWorker(frameId: number, imageView: Uint8Array): Promise<ImageBitmap> {
        const decoder = imageDecodingWorkers.filter(isDecoderReady)[0];

        return new Promise((resolve, reject) => {
            if (decoder) {
                decoder.onmessage = (e) => {
                    decoder.onmessage = null;
                    if (e.data.error) {
                        reject({ error: e.data.error, frameId });
                    } else {
                        const [imageBitmap] = e.data;
                        resolve(imageBitmap);
                    }
                };
                decoder.onerror = (e) => {
                    decoder.onmessage = null;
                    reject({ error: e.error, frameId });
                };
                decoder.postMessage([frameId, imageView.buffer, imageView.byteOffset, imageView.length],
                    [imageView.buffer]);
            } else {
                reject({ frameId, error: null });
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
                const headerSize = 8 * 8;

                const buffer: ArrayBuffer = evt.data;
                const imgView = new Uint8Array(buffer, headerSize);

                let i = 0;
                const dblView = new Float64Array(buffer, 0, headerSize);
                const frameId = dblView[i++] | 0;

                const header: FrameHeader = {
                    frameId,
                    frameTime: dblView[i++],
                    bandWidth: dblView[i++],
                    frameRate: dblView[i++],
                    renderDuration: dblView[i++],
                    transmitDuration: dblView[i++],
                    compressDuration: dblView[i++],
                    frameDuration: dblView[i++],
                }

                postResponse("onDecodeBegin", header);

                loadImageWithWorker(frameId, imgView).then(image => {
                    postResponse("onDecodeSuccess", { frameId, image }, [image]);
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
