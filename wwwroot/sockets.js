"use strict";
const worker = self;
let socket = null;
function sendToSocket(data) {
    if (socket) {
        socket.send(JSON.stringify(data));
    }
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
        }
        else {
            worker.postMessage(evt.data, [evt.data]);
        }
    };
    socket.onopen = evt => {
        worker.postMessage({ action: "onopen" });
    };
    socket.onerror = evt => {
        worker.postMessage({ action: "onerror" });
    };
    socket.onclose = () => {
        worker.postMessage({ action: "onclose" });
    };
}
worker.onmessage = evt => {
    const { action } = evt.data;
    switch (action) {
        case "start": {
            startSocket();
            break;
        }
        case "stop": {
            stopSocket();
            break;
        }
        default:
            sendToSocket(evt.data);
            break;
    }
};
startSocket();
