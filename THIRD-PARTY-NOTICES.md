# Third-party notices

The ARTEL SDK is released under the MIT License (see `LICENSE`). It ships the following third-party
component unmodified.

## websocket-sharp

- Files: `Packages/kr.artel.sdk/Runtime/Plugins/websocket-sharp.dll` and `websocket-sharp.xml`
- Upstream: https://github.com/sta/websocket-sharp
- Copyright holder recorded in the assembly: sta.blockhead
- Version: not recorded in this repository. Record it here when the binary is next replaced.
- License: MIT

```
The MIT License (MIT)

Copyright (c) sta.blockhead

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

The copyright line above has no year because the assembly records none. Replace it with the line from the
upstream `LICENSE.txt` for the version that is vendored.

## Unity packages

The SDK declares dependencies on Unity packages (`com.unity.webrtc`, `com.unity.textmeshpro`,
`com.unity.ugui`, `com.unity.modules.*`) under the Unity Companion Package License. They are fetched by
the Unity Package Manager and are not redistributed in this repository. `com.unity.nuget.newtonsoft-json`
and `com.unity.nuget.mono-cecil` are MIT.
