# InterviewX

# Objective 

Record market data coming from D. broker.

# Pinciples

The quotes are collected with sereral parallel webrequests.
All the deserialized data is added to a single queue which is poped by a unique consumer thread.
This consumer thread - only, and that's why it is unique - routes the data to a per-instrument queue.
When a queue seems ready to be flushed (several crits), the IO thread removes the in-memory data after it got the confirmation the data has been written to the per-instrument file.

The data is store as binary-fixed-size object in file.

# Limitations 

If the program  is stopped and restarted, the program cant request the D. servers to fill the market data missing during down-time.
There is no control about disk space.

# TODO tech
  * Adds try/catch for some critical blocks to avoid zombie threads.
  * Compress market data : use tick data to compress the fat decimal serial. Usually, price/tick_size fits in a 32b int, sometimes 24.
  * Add interfaces to ease testing (almost  everywhere)
  * Implement WebSocketMgr with strategies to handle providers request limits
  * Robustify AsyncController : use Interlocked to figure out the precise step of the start or stop workflows.
  * Add disk space control + hysteresis-based notifications when disk is almost full

