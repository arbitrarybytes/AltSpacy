# Third-party notices

AltSpacy uses no third-party packages at run time. The standalone download (`*-standalone.exe`) includes the .NET
runtime, Windows Desktop runtime (WPF and Windows Forms) and their components, which are licensed as follows.

## .NET runtime and Windows Desktop runtime

Source: <https://github.com/dotnet/runtime>, <https://github.com/dotnet/wpf>, <https://github.com/dotnet/winforms>

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

These runtimes include further third-party components, listed in their own notices files:

- <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>
- <https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT>
- <https://github.com/dotnet/winforms/blob/main/THIRD-PARTY-NOTICES.TXT>

## Development only (not shipped)

The tests use xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT) and coverlet (MIT).
