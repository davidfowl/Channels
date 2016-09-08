# Channels (Push based Streams)

[![Join the chat at https://gitter.im/davidfowl/Channels](https://badges.gitter.im/davidfowl/Channels.svg)](https://gitter.im/davidfowl/Channels?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

[![Unix CI](https://travis-ci.org/davidfowl/Channels.svg?branch=master)](https://travis-ci.org/davidfowl/Channels)
[![Windows CI](https://ci.appveyor.com/api/projects/status/github/davidfowl/Channels?svg=true)](https://ci.appveyor.com/project/davidfowl/Channels/branch/master)

## Disclaimer

This is still very much a work in progress. The APIs are not baked and are going through serious churn.

This is a new API that aims to replace `Stream`. `Stream` is a solid abstraction but it has some downsides:
- It always produces/consumes data into provided buffers. This has some interesting side effects:
  - If the user buffer is being passed into a native API (pinvoke) then the user buffer must be pinned. This may lead to heap fragmentation for long running async calls with pinned buffers.
  - If the underlying stream implementation is buffering then there is a buffer copy per call to `ReadAsync`/`WriteAsync`.
  - There's a `Task` allocation per call to `ReadAsync`/`WriteAsync` (this is cached in some cases but very hard to do it right).
- Using streams efficiently in networking scenarios requires buffer pooling. Nothing is provided out of the box to help here (there's a new BCL type called `ArrayPool<T>` to help fill that gap).
- It's hard to guarantee that `Stream` implementations don't buffer internally resulting in **multiple** copies.
- `Stream` implementations end up doing lots of book keeping to make sure it is never in an invalid state:
  - Overlapping calls to Read/WriteAsync
  - Calling sync APIs and async APIs in parallel
  - APIs that generally might be doing dangerous things that user code can corrupt (e.g. https://github.com/dotnet/corefx/blob/8064b55ca6b078120c5f6f287224d0562326c243/src/System.IO.Compression/src/System/IO/Compression/Deflater.cs#L26-L31)

Channels invert the polarity of streams by flowing buffers up to user code. The user never allocates a buffer. All memory allocations are handled by the channel implementation itself. The idea is that the channel implementation can do the least amount of work to get data from a source and flow it to the caller so there's *NEVER* a copy. 

See the sample for an example of the APIs:

https://github.com/davidfowl/Channels/blob/master/samples/Channels.Samples/Program.cs

## Writing custom channel implementations

Unlike `Stream`, it's unexpected that users would implement `IReadableChannel` and `IWritableChannel`. This library provides a default `MemoryPoolChannel`, which is an implementation of an `IReadableChannel` and `IWritableChannel` using a configurable `MemoryPool`. It is expected that channel implementors write into this channel when authoring an `IReadableChannel` and read from this channel when authoring an `IWritableChannel`.

To see an example of a custom `IReadableChannel` over Win32 files look at https://github.com/davidfowl/Channels/blob/master/samples/Channels.Samples/IO/ReadableFileChannel.cs

## MyGet Feed

You can access CI builds of channels using the following myget source:

```XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="Channels" value="https://www.myget.org/F/channels/api/v3/index.json" />
    <add key="dotnet-corefxlab" value="https://dotnet.myget.org/F/dotnet-corefxlab/api/v3/index.json" />
  </packageSources>
</configuration>
```
