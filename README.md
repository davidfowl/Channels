# Channels

This is a new API that aims to replace `Stream`. `Stream` is a solid abstraction but it has some downsides:
- It always produces/consumes data into provided buffers. This has some interesting side effects:
  - If the user buffer is being passed into a native API (pinvoke) then the user buffer must be pinned. This may lead to heap fragmentation for long running async calls with pinned buffers.
  - If the underlying stream implementation is buffering then there is a buffer copy per call to ReadAsync/WriteAsync.
  - There's a `Task` allocation per call to ReadAsync/WriteAsync (this is cached in some cases but very hard to do it right).
- Using streams efficiently in networking scenarios requires buffer pooling. Nothing is provided out of the box to help here (there's a new BCL type called `ArrayPool<T>` to help fill that gap).
- It's hard to guarantee that `Stream` implementations don't buffer internally resulting in multiple copies.

Channels invert the polarity of streams by flowing buffers up to user code. The user never allocates a buffer. All memory allocations are handled by the channel implementation itself. The idea is that the channel implementation can do the least amount of work to get data from a source and flow it to the caller so there's never a copy. 


See the sample for an example of the APIs:

https://github.com/davidfowl/Channels/blob/master/samples/Channels.Samples/Program.cs

## Writing custom channel implementations

Unlike `Stream`, it's unexpected that users would implement `IReadableChannel` and `IWritableChannel`. This library provides a default `MemoryPoolChannel`, which is an implementation of an `IReadableChannel` and `IWritableChannel` using a configurable `MemoryPool`. It is expected that channel implementors write into this channel when authoring an `IReadableChannel` and read from this channel when authoring an `IWritableChannel`.

To see an example of a custom `IReadableChannel` over Win32 files look at https://github.com/davidfowl/Channels/blob/master/samples/Channels.Samples/ReadableFileChannel.cs

## Backpressure

TODO
