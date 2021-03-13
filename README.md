# InterviewX

# Objective 

Record market data coming from D. broker.

# TODO tech
  * Compress market data : use tick data to compress the fat decimal serial
  * Add interfaces to ease testing (almost  everywhere)
  * Implement WebSocketMgr with strategies to handle providers request limits
  * Robustify AsyncController : use Interlocked to figure out the precise step of the start or stop workflows.
  *
