namespace UIThread {
    type DecodedImage = ImageBitmap | HTMLImageElement;

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
        const startBtn = document.getElementById("button-start") as HTMLButtonElement;
        const stopBtn = document.getElementById("button-stop") as HTMLButtonElement;
        const jankElem = document.getElementById("check-jank") as HTMLInputElement;

        const timelineRowCount = 8;
        const timelineRowHeight = 40;
        const timelineRowMargin = 4;

        let timelineWidth = 0;
        let timelineHeight = 0;

        let currentFrameId = 0;
        let currentFrameTime = -1;
        let imageFrameId = -1;

        // null when server is not ready yet.
        let maybeFrameSpec: FrameSpec | null = null;

        function timelineRowY(row: number) {
            const y = Math.round(row * timelineRowHeight + timelineRowMargin + row * timelineRowMargin);
            return y;
        }

        const timelineViewHeight = timelineRowY(timelineRowCount);

        let timelineStartTime = NaN;
        let frameRect = frameElem.getBoundingClientRect();

        let oldAngle = 0;

        let socketWorker = new Worker("socket.js");

        function log(text: string) {
            const line = document.createElement("code");
            line.innerText = text;
            logElem.appendChild(line);
        }

        function setupPixelSharpCanvas(canvas: HTMLCanvasElement, cssWidth?: number, cssHeight?: number) {
            cssWidth = cssWidth || canvas.clientWidth;
            cssHeight = cssHeight || canvas.clientHeight;
            const scale = window.devicePixelRatio;
            canvas.width = (cssWidth * scale) | 0;
            canvas.height = (cssHeight * scale) | 0;

            const ctx = canvas.getContext("2d")!;
            const scaleX = canvas.width / cssWidth;
            const scaleY = canvas.height / cssHeight;
            ctx.setTransform(scaleX, 0, 0, scaleY, 0, 0);

            return ctx;
        }

        function resizeStats() {
            const ctx = setupPixelSharpCanvas(statsElem);
            ctx.font = "12px monospace";
            ctx.textAlign = "left";
        }

        function clearTimeline() {
            timelineWidth = Math.floor(window.innerWidth);
            timelineHeight = timelineViewHeight;

            timelineElem.style.width = timelineWidth + "px";
            timelineElem.style.height = timelineHeight + "px";

            const ctx = setupPixelSharpCanvas(timelineElem, timelineWidth, timelineHeight);
            ctx.font = "7px 'Arial Narrow', sans-serif";
            ctx.textAlign = "left";

            ctx.clearRect(0, 0, timelineWidth, timelineHeight);

            for (let row = 0; row < timelineRowCount; ++row) {
                const y = timelineRowY(row);
                ctx.fillStyle = "#333";
                ctx.fillRect(0, y, timelineWidth, timelineRowHeight);
            }
        }

        function getTimelineXY(timeMS: number): [number, number] {
            const pixelsPerSecond = timelineWidth * 3;
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

            const ctx = timelineElem.getContext("2d")!;
            ctx.fillStyle = "black";
            ctx.fillRect(x - 1, y - timelineRowMargin, 3, timelineRowHeight);
        }

        let timelineHandle = 0;

        function updateButtons() {
            animBtn.disabled = !isConnected() || !isTimelineStopped();
            mouseBtn.disabled = !isConnected() || !isTimelineStopped();
            startBtn.disabled = isConnected();
            stopBtn.disabled = !isConnected();
        }

        function startTimeline() {
            clearTimeline();

            resizeStats();

            timelineStartTime = performance.now();

            function loop(timeMS: DOMHighResTimeStamp) {
                drawTimeMarker(timeMS);
                timelineHandle = requestAnimationFrame(loop);
            }

            timelineHandle = requestAnimationFrame(loop);

            updateButtons();
        }

        function stopClient() {
            log("stopping");

            stopRenderLoop();

            cancelAnimationFrame(timelineHandle);

            timelineHandle = 0;
            timelineStartTime = NaN;
            currentFrameId = 0;
            currentFrameTime = -1;
            imageFrameId = -1;

            canvasElem.onmousedown = null;
            canvasElem.onmousemove = null;
            canvasElem.onmouseup = null;

            updateButtons();
        }

        function onDisconnected() {
            log("disconnected");
            maybeFrameSpec = null;
            stopClient();
        }

        function isTimelineStopped() {
            return isNaN(timelineStartTime);
        }

        function isConnected() {
            return !!maybeFrameSpec;
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

            const ctx = timelineElem.getContext("2d")!;
            const fill = eventColor[kind] || "yellow";;
            ctx.fillStyle = fill;
            ctx.fillRect(x, y, 1, timelineRowHeight);

            //ctx.save();
            //ctx.translate(x, y);
            //ctx.rotate(Math.PI / 2);
            ctx.textBaseline = "top";
            const text = frameId.toString(10);
            const textX = x;
            const textY = y + kind * (timelineRowHeight - 8) / eventCount;

            const tm = ctx.measureText(text);
            ctx.fillStyle = fill;
            ctx.fillRect(textX, textY, tm.width + 2, 10);
            ctx.fillStyle = "black";
            ctx.fillText(frameId.toString(10), textX + 1, textY + 1);
            //ctx.restore();
        }

        function sendJson(action: string, payload: any) {
            socketWorker.postMessage({ action, payload });
        }

        function sendCanvasMousePos(ev: MouseEvent) {
            // if (!socket || socket.readyState !== WebSocket.OPEN || ev.buttons === 0)
            //     return;

            if (ev.buttons === 0)
                return;

            if (!maybeFrameSpec)
                return;

            let { width, height, spinDurationSec } = maybeFrameSpec;

            // Convert to relative coordinates, relative to center of image.
            // TODO: This doesn't take any CSS transform into account!
            const posX = (ev.clientX - frameRect.left - width / 2);// / (imageRect.right - imageRect.left);
            const posY = (ev.clientY - frameRect.top - height / 2); // / (imageRect.top - imageRect.bottom);

            const radians = Math.atan2(posY, posX);

            const circleTime = 1000 * radians * spinDurationSec / (Math.PI * 2);

            advanceFrameTime(maybeFrameSpec, currentFrameId, performance.now(), circleTime);
        }

        // Render disc with units
        function renderClock(spec: FrameSpec, clock: HTMLCanvasElement) {
            let { width, height, center, radius, spinSubdivisions, spinDurationSec } = spec;

            clock.width = width;
            clock.height = height;

            let context = clock.getContext("2d")!;

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
            if (jankElem.checked) {
                [imageElem, canvasElem].forEach(e => {
                    e.style.transformOrigin = "50% 50%";
                    e.style.transform = `rotate3d(0,0,1, ${(-degrees).toFixed(4)}deg)`;
                });
            }
        }

        function onServerReady(spec: FrameSpec) {
            let { width, height, center, radius } = maybeFrameSpec = spec;

            frameElem.style.width = frameElem.style.minWidth = `${width}px`;
            frameElem.style.height = frameElem.style.minHeight = `${height}px`;
            // frameElem.style.clipPath = `circle(${center + radius}px at center)`;

            imageElem.width = clockElem.width = canvasElem.width = width;
            imageElem.height = clockElem.height = canvasElem.height = height;

            updateButtons();

            startBtn.onclick = () => {
                stopClient();
                sendJson("START", null);
            };

            stopBtn.onclick = () => {
                stopClient();
                sendJson("STOP", null);
            };

            animBtn.onclick = () => {
                stopClient();

                if (maybeFrameSpec) {
                    startTimeline();
                    startRenderLoop(maybeFrameSpec);
                }
            };

            mouseBtn.onclick = () => {
                stopClient();

                if (maybeFrameSpec) {
                    startTimeline();
                    canvasElem.onmousedown = sendCanvasMousePos;
                    canvasElem.onmousemove = sendCanvasMousePos;
                    canvasElem.onmouseup = sendCanvasMousePos;
                }
            };

            frameRect = frameElem.getBoundingClientRect();

            renderClock(spec, clockElem);

            log("server ready");
        }

        function timeToDegrees(frameTime: number, spec: FrameSpec) {
            return (frameTime * 0.360 / spec.spinDurationSec) % 360;
        }

        function advanceFrameTime(spec: FrameSpec, frameId: number, frameTime: number, circleTime: number): number | null {
            // In some browsers, performance.now() has ms precision (Spectre mitigation?)
            // And it seems more than one mouse event can occur within the same ms.
            if (currentFrameTime >= frameTime)
                return null;

            currentFrameTime = frameTime;
            currentFrameId += 1;

            drawTimeEvent(EventKind.Request, frameId, frameTime);

            // console.log(`OUT: ${frameId} ${performance.now() | 0}ms`);

            sendJson("TICK", { frameTime, circleTime, frameId });

            const context = canvasElem.getContext("2d")!;
            context.save();

            let { width, height, center, radius } = spec;
            context.translate(width * 0.5, height * 0.5);

            let degrees = timeToDegrees(circleTime, spec);
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

        const renderLoopSampleEvery = 1;

        function startRenderLoop(spec: FrameSpec) {
            function loop(frameTime: number) {
                if ((currentFrameId % renderLoopSampleEvery) === 0) {
                    const degrees = advanceFrameTime(spec, currentFrameId, frameTime, frameTime);
                    if (degrees) {
                        counterRotate(degrees);
                    }
                }
                renderLoopHandle = requestAnimationFrame(loop);
            }

            renderLoopHandle = requestAnimationFrame(loop);
        }

        function stopRenderLoop() {
            cancelAnimationFrame(renderLoopHandle);
            renderLoopHandle = 0;
        }

        window.onresize = () => {
            frameRect = frameElem.getBoundingClientRect();
            resizeStats();
            clearTimeline();
        }

        socketWorker.onmessage = evt => {
            const { action, payload } = evt.data;
            if (action) {
                switch (action) {
                    case "onopen": {
                        log("connected");
                        break;
                    }

                    case "onerror": {
                        log("Socket error");
                        stopClient();
                        break;
                    }

                    case "onclose": {
                        onDisconnected();
                        break;
                    };

                    case "onDecodeRequest": {
                        const { frameId } = payload as WithFrameId;
                        drawTimeEvent(EventKind.Dequeued, frameId);
                        break;
                    }

                    case "onDecodeBegin": {
                        const { frameId, frameTime } = payload as SegmentHeader;
                        drawTimeEvent(EventKind.Response, frameId);

                        // Update stats
                        if (currentFrameTime >= 0 && frameTime > currentFrameTime) {
                            log(`Frames out of order! ${frameTime}#${frameId} arrived after current ${currentFrameTime}#${currentFrameId}!`);
                        }

                        const stats = { ...payload, latency: performance.now() - payload.frameTime };

                        let y = 10;
                        let x = 10;

                        const ctx = statsElem.getContext("2d")!
                        ctx.clearRect(0, 0, statsElem.width, statsElem.height);

                        ctx.fillStyle = "black";

                        for (const key in stats) {
                            const line = `${(key as any).padStart(20)}: ${stats[key].toFixed(3).padStart(8, "0")}\n`;
                            ctx.fillText(line, x, y);
                            y += 12;
                        }

                        break;
                    }

                    case "onDecodeSuccess": {
                        const { frameId, segments } = payload as JitterBuffer
                        if (segments) {
                            drawTimeEvent(EventKind.Decoded, frameId);
                            if (frameId > imageFrameId) {
                                const ctx = imageElem.getContext("2d")!;
                                ctx.imageSmoothingEnabled = false;
                                for (const segment of segments) {
                                    const { image, header } = segment;
                                    ctx.drawImage(image, header.segmentX, header.segmentY);
                                }

                                // TODO: Close might be slow, we call this in the next tick.
                                new Promise(resolve => {
                                    for (const segment of segments) {
                                        const { image } = segment;
                                        if ("close" in image) {
                                            image.close();
                                        }
                                    }
                                    resolve();
                                });
                            }
                        }
                        break;
                    }

                    case "onDecodeFailure": {
                        const { frameId, error } = payload as { frameId: number, error?: string };
                        if (error) {
                            console.error(`Failed to decode frame ${frameId}`, error);
                            drawTimeEvent(EventKind.Failed, frameId);
                        } else {
                            drawTimeEvent(EventKind.Skipped, frameId);
                        }
                        break;
                    }

                    case "READY":
                        onServerReady(payload);
                        break;
                }
            }
        };

        clearTimeline();
        resizeStats();
    }

    document.addEventListener("DOMContentLoaded", main);
}

