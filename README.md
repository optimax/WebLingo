# WebLingo

![WebLingo logo](Graphics/weblingo-logo-light.svg)

WebLingo is a JavaScript-inspired scripting language for command-line programs and server-side web applications.
It aims to be a language with no surprises. Conversions are explicit, conditions require Booleans, and integer
overflow raises an error. There is no prototype-based inheritance. The language is supposed to be boring and you probably already know how to program in it.

Write scripts and modules in `.wlg` files. For web applications, **WASP** (Web Application Server Pages) lets you
embed WebLingo in HTML using `.wasp` templates, with automatic escaping for interpolated values based on where
they appear in the page. WebLingo is currently pre-alpha.

## Try it

On Windows, extract the self-contained WebLingo ZIP for your processor and double-click **Install WebLingo.bat**.
Setup can register `.wlg` files and install the debugger for VS Code. [Windows installation guide](Installers/Windows/readme.md)

With the .NET 10 SDK installed, run the included hello-world script from the repository root.

```sh
cd WebLingo
dotnet run --project src/WebLingo.Cli -- run samples/HelloWorld/hello.wlg
```

## Learn more

- [Getting started with WebLingo](Documents/getting-started-guide.md)
- [Language reference](Documents/weblingo-language-reference.md) and [example scripts](Examples.wlg/)
- [Standard library reference](Documents/weblingo-standard-library-reference.md)
- [Testing WebLingo code](Documents/weblingo-standard-library-reference.md#30-stdtest)
- [WASP user manual](Documents/wasp-user-manual.md) and [example sites](Examples.wasp/)
- [Build and test instructions](WebLingo/README.md)
