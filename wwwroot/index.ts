interface FrameSpec {
    width: number;
    height: number;
    radius: number;// TODO: Rename to outerRadius
    center: number; // TODO: Rename to innerRadius
    spinDurationSec: number;
    spinSubdivisions: number;
}

enum EventKind {
    Request,
    Dequeued,
    Response,
    Decoded,
    Skipped,
    Failed,
}

function main() {

    const timelineElem = document.getElementById("timeline") as HTMLCanvasElement;
    const canvasElem = document.getElementById("canvas") as HTMLCanvasElement;
    const imageElem = document.getElementById("image") as HTMLCanvasElement;
    const clockElem = document.getElementById("clock") as HTMLCanvasElement;
    const logElem = document.getElementById("log") as HTMLElement;
    const frameElem = document.getElementById("frame") as HTMLElement;
    const statsElem = document.getElementById("stats") as HTMLCanvasElement;
    const animBtn = document.getElementById("button-anim") as HTMLButtonElement;
    const mouseBtn = document.getElementById("button-mouse") as HTMLButtonElement;
    const stopBtn = document.getElementById("button-stop") as HTMLButtonElement;

    const timelineRowCount = 8;
    const timelineRowHeight = 40;
    const timelineRowMargin = 4;

    let timelineWidth = 0;
    let timelineHeight = 0;

    let currentFrameId = 0;
    let imageFrameId = -1;

    function timelineRowY(row: number) {
        const y = Math.round(row * timelineRowHeight + timelineRowMargin + row * timelineRowMargin);
        return y;
    }

    const timelineViewHeight = timelineRowY(timelineRowCount);

    let timelineStartTime = NaN;
    let frameRect = frameElem.getBoundingClientRect();

    let frameSpec: FrameSpec = {} as any;

    let oldAngle = 0;

    let socket: WebSocket = null;

    type ImageDecoder = Worker | HTMLImageElement;

    function createImageDecoder(id: number): ImageDecoder {
        // createImageBitmap not available (Edge) => use image on UI thread 
        return "createImageBitmap" in window
            ? new Worker("decoder.js")
            : document.createElement("img");
    }

    function isImage(d: ImageDecoder): d is HTMLImageElement {
        return d instanceof HTMLImageElement;
    }

    function isDecoderReady(d: ImageDecoder) {
        return isImage(d) ? !d.onload : !d.onmessage;
    }

    const imageDecodingWorkers = [...Array(3)].map(createImageDecoder);

    function loadImageWithWorker(frameId: number, imageView: Uint8Array): Promise<HTMLImageElement> {
        const decoder = imageDecodingWorkers.filter(isDecoderReady)[0];

        return new Promise((resolve, reject) => {
            if (decoder) {
                if (isImage(decoder)) {
                    const imageBlob = new Blob([imageView], { type: "image/jpeg" });
                    decoder.src = URL.createObjectURL(imageBlob);
                    decoder.onload = () => {
                        decoder.onload = null;
                        resolve(decoder);
                        URL.revokeObjectURL(decoder.src);
                    };
                    decoder.onerror = () => {
                        decoder.onload = null;
                        reject({ frameId, error: null });
                        URL.revokeObjectURL(decoder.src);
                    }
                } else {
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
                }
            } else {
                reject({ frameId, error: null });
            }
        });
    }

    function log(text: string) {
        const line = document.createElement("code");
        line.innerText = text;
        logElem.appendChild(line);
    }

    function resizeStats() {
        const rect = statsElem.getBoundingClientRect();
        const scale = window.devicePixelRatio;
        statsElem.width = rect.width * scale;
        statsElem.height = rect.height * scale;

        const ctx = statsElem.getContext("2d");
        ctx.font = "12px monospace";
        ctx.textAlign = "left";
        ctx.setTransform(scale, 0, 0, scale, 0, 0);
    }

    function clearTimeline() {
        const scale = window.devicePixelRatio;

        timelineWidth = Math.floor(window.innerWidth);
        timelineHeight = timelineViewHeight;

        timelineElem.width = timelineWidth * scale;
        timelineElem.height = timelineHeight * scale;

        timelineElem.style.width = timelineWidth + "px";
        timelineElem.style.height = timelineHeight + "px";

        const ctx = timelineElem.getContext("2d");
        ctx.font = "6px sans-serif";
        ctx.imageSmoothingEnabled = false;

        ctx.setTransform(scale, 0, 0, scale, 0, 0);

        ctx.clearRect(0, 0, timelineWidth, timelineHeight);

        for (let row = 0; row < timelineRowCount; ++row) {
            const y = timelineRowY(row);
            ctx.fillStyle = "#333";
            ctx.fillRect(0, y, timelineWidth, timelineRowHeight);
        }
    }

    function getTimelineXY(timeMS: number): [number, number] {
        const pixelsPerSecond = timelineWidth * 2;
        const offset = (timeMS - timelineStartTime) * pixelsPerSecond / 1000;
        const row = Math.floor(offset / timelineWidth);
        const x = Math.round(offset % timelineWidth);
        const y = timelineRowY(row);
        return [x, y];
    }

    function moveTimeline(timeMS: number) {
        if (isNaN(timelineStartTime) || getTimelineXY(timeMS)[1] >= timelineViewHeight) {
            timelineStartTime = Math.floor(timeMS / 1000) * 1000;
            clearTimeline();
        }
    }

    function drawTimeMarker(timeMS: number) {
        if (isTimelineStopped())
            return;

        moveTimeline(timeMS);

        const [x, y] = getTimelineXY(timeMS);

        const ctx = timelineElem.getContext("2d");
        ctx.fillStyle = "black";
        ctx.fillRect(x - 1, y - timelineRowMargin, 3, timelineRowHeight);
    }

    let timelineHandle = 0;

    function startTimeline() {
        clearTimeline();

        timelineStartTime = performance.now();

        function loop(timeMS: DOMHighResTimeStamp) {
            drawTimeMarker(timeMS);
            timelineHandle = requestAnimationFrame(loop);
        }

        timelineHandle = requestAnimationFrame(loop);
    }

    function stopTimeline() {
        cancelAnimationFrame(timelineHandle);
        timelineHandle = 0;
        timelineStartTime = NaN;
        currentFrameId = 0;
        imageFrameId = -1;
    }

    function isTimelineStopped() {
        return isNaN(timelineStartTime);
    }

    const eventColor = {
        [EventKind.Request]: "#8F8",
        [EventKind.Dequeued]: "#888",
        [EventKind.Response]: "#48F",
        [EventKind.Decoded]: "#8FF",
        [EventKind.Skipped]: "orange",
        [EventKind.Failed]: "red"
    }

    const eventCount = Object.keys(eventColor).length / 2;

    function drawTimeEvent(kind: EventKind, frameId: number, timeMS: number = performance.now()) {
        if (isTimelineStopped())
            return;

        if (typeof frameId !== "number")
            return;

        moveTimeline(timeMS);

        const [x, y] = getTimelineXY(timeMS);

        const ctx = timelineElem.getContext("2d");
        ctx.fillStyle = eventColor[kind] || "yellow";
        ctx.fillRect(x, y, 1, timelineRowHeight);

        ctx.save();
        ctx.translate(x, y);
        ctx.rotate(Math.PI / 2);
        ctx.fillText(frameId.toString(10), kind * (timelineRowHeight - 4) / eventCount, -2);
        ctx.restore();
    }

    function sendJson(action: string, payload: any) {
        socket.send(JSON.stringify({ action, payload }));
    }

    function sendCanvasMousePos(kind: number, ev: MouseEvent) {
        if (!socket || socket.readyState !== WebSocket.OPEN || ev.buttons === 0)
            return;

        let { width, height, center, radius, spinDurationSec, spinSubdivisions } = frameSpec;

        // Convert to relative coordinates, relative to center of image.
        // TODO: This doesn't take any CSS transform into account!
        const posX = (ev.clientX - frameRect.left - width / 2);// / (imageRect.right - imageRect.left);
        const posY = (ev.clientY - frameRect.top - height / 2); // / (imageRect.top - imageRect.bottom);

        const radians = Math.atan2(posY, posX);

        const circleTime = 1000 * radians * spinDurationSec / (Math.PI * 2);

        sendFrameTime(currentFrameId, performance.now(), circleTime);

        currentFrameId += 1;

        // sendJson("MOUSE", { kind, posX, posY })
    }

    // Render disc with units
    function renderClock(clock: HTMLCanvasElement) {
        let { width, height, center, radius, spinSubdivisions, spinDurationSec } = frameSpec;

        clock.width = width;
        clock.height = height;

        let context = clock.getContext("2d");

        context.clearRect(0, 0, width, height);

        context.fillStyle = "green";
        context.translate(width * 0.5, height * 0.5);

        context.fillStyle = "#000";
        for (let i = 0; i < spinSubdivisions; ++i) {
            if (i % 5 != 0) {
                context.save();
                context.rotate(i * Math.PI * 2 / spinSubdivisions);
                context.fillRect(center, -1, radius, 2);
                context.restore();
            }
        }

        context.fillStyle = "#111";
        for (let i = 0; i < spinSubdivisions; ++i) {
            if (i % 5 == 0) {
                context.save();
                context.rotate(i * Math.PI * 2 / spinSubdivisions);
                context.fillRect(center, -2, radius, 4);
                context.restore();
            }
        }

        context.fillStyle = "#222";
        for (let i = 0; i < spinDurationSec; ++i) {
            context.save();
            context.rotate(i * Math.PI * 2 / spinDurationSec);
            context.fillRect(center, -3, radius, 6);
            context.restore();
        }
    }

    function counterRotate(degrees: number) {
        [imageElem, canvasElem].forEach(e => {
            e.style.transformOrigin = "50% 50%";
            e.style.transform = `rotate3d(0,0,1, ${(-degrees).toFixed(4)}deg)`;
        });
    }

    function setupRenderElements() {
        let { width, height, center, radius } = frameSpec;

        frameElem.style.width = frameElem.style.minWidth = `${width}px`;
        frameElem.style.height = frameElem.style.minHeight = `${height}px`;
        frameElem.style.clipPath = `circle(${center + radius}px at center)`;

        imageElem.width = clockElem.width = canvasElem.width = width;
        imageElem.height = clockElem.height = canvasElem.height = height;

        animBtn.disabled = false;
        mouseBtn.disabled = false;

        animBtn.onclick = () => {
            animBtn.disabled = true;
            mouseBtn.disabled = true;
            stopBtn.disabled = false;
            startTimeline();
            startRenderLoop();
        };

        mouseBtn.onclick = () => {
            startTimeline();
            animBtn.disabled = true;
            mouseBtn.disabled = true;
            stopBtn.disabled = false;
            canvasElem.onmousedown = (ev) => sendCanvasMousePos(1, ev);
            canvasElem.onmousemove = (ev) => sendCanvasMousePos(0, ev);
            canvasElem.onmouseup = (ev) => sendCanvasMousePos(-1, ev);
        };

        stopBtn.onclick = () => {
            animBtn.disabled = true;
            mouseBtn.disabled = true;
            stopBtn.disabled = true;
            stopTimeline();
            stopRenderLoop();
            socket.close();
            connect();
        };

        frameRect = frameElem.getBoundingClientRect();

        renderClock(clockElem);
    }

    function timeToDegrees(frameTime: number) {
        return (frameTime * 0.360 / frameSpec.spinDurationSec) % 360;
    }

    function sendFrameTime(frameId: number, frameTime: number, circleTime: number): number {
        drawTimeEvent(EventKind.Request, frameId, frameTime);

        // console.log(`OUT: ${frameId} ${performance.now() | 0}ms`);

        sendJson("TICK", { frameTime, circleTime, frameId });

        const context = canvasElem.getContext("2d");
        context.save();

        let { width, height, center, radius } = frameSpec;
        context.translate(width * 0.5, height * 0.5);

        let degrees = timeToDegrees(circleTime);
        let newAngle = Math.PI * degrees / 180;

        context.save();
        context.rotate(oldAngle);
        context.clearRect(center - 1, -4, radius + 2, 8);
        context.restore();

        oldAngle = newAngle;

        context.save();
        context.rotate(newAngle);
        context.fillStyle = "green";
        context.fillRect(center, -2, radius, 4);
        context.restore();

        context.restore();

        return degrees;
    }

    let renderLoopHandle = 0;

    function startRenderLoop() {
        function loop(frameTime: number) {
            //if ((currentFrameId & 1) === 0) {
            const degrees = sendFrameTime(currentFrameId, frameTime, frameTime);
            counterRotate(degrees);
            //}

            currentFrameId += 1;

            if (socket.readyState === WebSocket.OPEN) {
                renderLoopHandle = requestAnimationFrame(loop);
            }
        }

        renderLoopHandle = requestAnimationFrame(loop);
    }

    function stopRenderLoop() {
        cancelAnimationFrame(renderLoopHandle);
        renderLoopHandle = 0;
    }

    function connect() {
        log("connecting");

        let scheme = document.location.protocol === "https:" ? "wss" : "ws";
        let port = document.location.port ? (":" + document.location.port) : "";
        let url = scheme + "://" + document.location.hostname + port + "/renderer";
        socket = new WebSocket(url);
        socket.binaryType = "arraybuffer";

        socket.onmessage = evt => {
            if (typeof evt.data === "string") {
                const { action, payload } = JSON.parse(evt.data);
                switch (action) {
                    case "READY":
                        frameSpec = payload;
                        setupRenderElements();
                        break;
                    case "STATS": {
                        const frameId = payload.frameId;
                        drawTimeEvent(EventKind.Dequeued, frameId);
                        //if (stats.frameTime >= payload.frameTime) {
                        //  log(`Frames out of order! ${payload.frameTime} arrived after ${stats.frameTime}!`);
                        //}
                        const stats = { ...payload, latency: performance.now() - payload.frameTime };

                        let y = 10;
                        let x = 10;

                        const ctx = statsElem.getContext("2d");
                        ctx.clearRect(0, 0, statsElem.width, statsElem.height);

                        ctx.fillStyle = "black";

                        for (const key in stats) {
                            const line = `${(key as any).padStart(20)}: ${stats[key].toFixed(3).padStart(8, "0")}\n`;
                            ctx.fillText(line, x, y);
                            y += 12;
                        }

                        break;
                    }
                }
            } else {
                const buffer: ArrayBuffer = evt.data;
                const imgView = new Uint8Array(buffer, 4);
                const intView = new Int32Array(buffer, 0, 4);
                const frameId = intView[0];

                drawTimeEvent(EventKind.Response, frameId);

                loadImageWithWorker(frameId, imgView).then(image => {
                    drawTimeEvent(EventKind.Decoded, frameId);
                    if (image && frameId > imageFrameId) {
                        const ctx = imageElem.getContext("2d");
                        ctx.imageSmoothingEnabled = false;
                        ctx.drawImage(image, 0, 0);
                    }
                }, ({ frameId, error }) => {
                    if (error) {
                        console.error(`Failed to decode frame ${frameId}`, error);
                        drawTimeEvent(EventKind.Failed, frameId);
                    } else {
                        drawTimeEvent(EventKind.Skipped, frameId);
                    }
                });
            }
        };
        socket.onopen = evt => {
            log("connected");
        }
        socket.onerror = evt => {
            stopTimeline();
            stopRenderLoop();
            log("Socket error");
        };
        socket.onclose = () => {
            stopTimeline();
            stopRenderLoop();
            log("disconnected");
        };
    }

    window.onresize = () => {
        frameRect = frameElem.getBoundingClientRect();
        resizeStats();
        clearTimeline();
    }

    connect();
    clearTimeline();
    resizeStats();
}

document.addEventListener("DOMContentLoaded", main);
