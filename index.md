---
_layout: landing
---

# Snappier

Snappier is a pure C# port of Google's [Snappy](https://github.com/google/snappy) compression algorithm. It is designed with speed as the primary goal, rather than compression ratio, and is ideal for compressing network traffic. Please see [the Snappy README file](https://github.com/google/snappy/blob/master/README.md) for more details on Snappy.

## Project Goals

The Snappier project aims to meet the following needs of the .NET community.

- Cross-platform C# implementation for Linux and Windows, without P/Invoke or special OS installation requirements
- Compatible with .NET 4.6.1 and later and .NET 6 and later
- Use .NET paradigms, including asynchronous stream support
- Full compatibility with both block and stream formats
- Near C++ level performance
  - Note: This is only possible on .NET 6 and later with the aid of [Span&lt;T&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.span-1?view=net-9.0) and [System.Runtime.Intrinsics](https://devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics/)
  - .NET 4.6.1 is the slowest
- Keep allocations and garbage collection to a minimum using buffer pools

## License

Copyright &copy; 2011-2024, Google, Inc. and Snappier Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

* Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
* Neither the name of Google, Inc., any Snappier authors, nor the 
names of its contributors may be used to endorse or promote products 
derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## Other Projects

There are other projects available for C#/.NET which implement Snappy compression.

- [Snappy.NET](https://snappy.machinezoo.com/) - Uses P/Invoke to C++ for great performance. However, it only works on Windows, and is a bit heap allocation heavy in some cases. It also hasn't been updated since 2014 (as of 10/2020). This project may still be the best choice if your project is on the legacy .NET Framework on Windows, where Snappier is much less performant.
- [IronSnappy](https://github.com/aloneguid/IronSnappy) - Another pure C# port, based on the Golang implementation instead of the C++ implementation.
