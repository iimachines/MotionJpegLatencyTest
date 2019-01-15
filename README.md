# MotionJpegLatencyTest
A quick and dirty experiment to see if it is possible to have low-latency JPEG streaming using Websockets

# Installation

* Should run on any platform, but only tested on Windows

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

    * <code style="color:#8F8;background:black;">|</code>: Client sends `animation-frame` request 
    * <code style="color:#888;background:black;">|</code>: Server started processing the request 
    * <code style="color:#48F;background:black;">|</code>: Server image data is received by client
    * <code style="color:#8FF;background:black;">|</code>: Client has decoded image
    * <code style="color:#F84;background:black;">|</code>: Client has skipped a frame because all buffered images were still decoding
    * Each small black-tick represents 1/60th of a second
    
# The problem

When running this on a fast PC without any network, one would expect very low latency.

Indeed, running this with Microsoft EDGE on my Windows 10 PC, hardly any latency is noticed. Firefox has a bit of latency

However, when using Google Chrome, strange stuff happens. In the timeline one can observe that the server starts receives the render requests way to late, always delayed by a single frame. 

This might be related to this [Chromium bug](https://bugs.chromium.org/p/chromium/issues/detail?id=692257&q=websocket%20delay&colspec=ID%20Pri%20M%20Stars%20ReleaseBlock%20Component%20Status%20Owner%20Summary%20OS%20Modified)



