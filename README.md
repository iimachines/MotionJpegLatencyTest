# MotionJpegLatencyTest
A quick and dirty experiment to see if it is possible to have low-latency JPEG streaming using Websockets

# Installation

* Should run on any platform, but only tested on Windows

  * It seems that on MacOS and Linux, you will need to install [libgdiplus](https://github.com/mono/libgdiplus)

* Install [.NET Core 2.1](https://dotnet.microsoft.com/download/dotnet-core/2.1)

* `cd` into the cloned folder 

* Run `dotnet run` 

* Open a browser to `http://localhost:5000`

* Click on the `Test animation-frame` button

  * This will send browser `animation-frame` events to the Kestrel server.
  * The server will render an image, using a pipeline of several threads.
  * The images are send as `1280x720` Turbo-JPEGs through a websocket to the client.
  * The client uses two image buffers, and when the latest one is decoded, paints this onto a HTML5 canvas.
  * The distance between the green line and the black line is the latency
  * The "clock" overlay has intervals of 1/60th of a second.
 
* The timeline above will draw a marker for several events:


    * ![](https://placehold.it/5x15/8F8/000000?text=+) Client sends `animation-frame` request 
    * ![](https://placehold.it/5x15/888/000000?text=+) Server started processing the request 
    * ![](https://placehold.it/5x15/48F/000000?text=+) Server image data is received by client
    * ![](https://placehold.it/5x15/8FF/000000?text=+) Client has decoded image
    * ![](https://placehold.it/5x15/F84/000000?text=+) Client has skipped a frame because all buffered images were still decoding
    * Each small black-tick represents 1/60th of a second
    
# The problem

When running this on a fast PC without any network, one would expect very low latency.

Indeed, running this with Microsoft EDGE on my Windows 10 PC, hardly any latency is noticed. Firefox has a bit of latency. In the timeline one can observe that the render requests (gray bars) are immediately processed by the server after the requests are send from the client (green bars). 

However, when using Google Chrome <sub><sup>(I tested 71.0.3578.98 and 73.0.3672.0)</sup></sub>, latency is higher, and strange stuff is observed in the timeline... Notice how the server receives (gray bar) render requests **way to late**, while these are just very small websocket messages. It is as if Chrome is queuing the messages. Note that when not transmitting the image from the server, no delay is noticed in the timeline, as if Chrome's websocket is not full-duplex.

This might be related to this [Chromium bug](https://bugs.chromium.org/p/chromium/issues/detail?id=692257&q=websocket%20delay&colspec=ID%20Pri%20M%20Stars%20ReleaseBlock%20Component%20Status%20Owner%20Summary%20OS%20Modified)

Or this might be a  bug in this *very quick and dirty rough around the edges* experiment ;-)

Nevertheless the different browser behavior is weird.

# UPDATE 1

After ricea@chromium.org commented on the issue, I did some profiling, and it turned out Chrome decodes images on the main UI thread, blocking the websocket message.

I did not yet put the websocket on a web-worker as was suggested, instead I used `createImageBitmap` on multiple web-workers to decode and transfer the image back to the main thread.

This significantly improves the delay and latency of this experiment on Chrome, but after a while Chrome crashes with "aw snap" (most likely because the experiment now creates 60 image bitmaps per second, but that is just a guess).

# UPDATE 2

The crash above is indeed caused by leaking memory. It turns out that explicitly calling `close` on the image bitmap fixes the leak, Chrome is not garbage collecting the bitmap images...
 







