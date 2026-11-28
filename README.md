# WebLingo

![WebLingo logo](Graphics/weblingo-logo-light.svg)

WebLingo is a JavaScript-inspired scripting language for command-line programs and server-side web applications.
It aims to be a language with no surprises. Conversions are explicit, conditions require Booleans, and integer
overflow raises an error. There is no prototype-based inheritance. The language is supposed to be boring and you probably already know how to program in it.

Write scripts and modules in `.wlg` files. For web applications, **WASP** (Web Application Server Pages) lets you
embed WebLingo in HTML using `.wasp` templates, with automatic escaping for interpolated values based on where
they appear in the page.

## Try it

Download WebLingo for your system from the [releases page](https://github.com/optimax/WebLingo/releases):

- **Windows:** WebLingo's self-contained zip for your processor, usually `…-win-x64-self-contained.zip`. Extract it and
  double-click **Install WebLingo.bat**. Setup can register `.wlg` files and install the debugger for VS Code and
  Visual Studio. To remove WebLingo, double-click **Uninstall WebLingo.bat** in its installation folder or the
  extracted zip. [Windows installation guide](Installers/Windows/readme.md)
- **WASP on Windows:** to serve web pages, install WASP's zip of the same version, processor and kind after WebLingo's,
  for IIS or for `wasp serve` alone. [WASP's installation guide](Installers/Windows/wasp-readme.md)
- **Linux:** the archive or the Debian package for your processor, which installs `weblingo` and `wasp` together.
  [Linux installation guide](Installers/Linux/readme.md)

Then, in a new console, `weblingo --version` shows what you installed. Releases are betas for now: the language, its
standard modules and a site's configuration may still change from one to the next. To report a bug or ask a question,
see [CONTRIBUTING.md](CONTRIBUTING.md).

With the .NET 10 SDK installed, you can instead run the included hello-world script from a checkout's root.

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

## License

WebLingo is free and open source, under the [MIT License](LICENSE). The packages it ships, their licenses and their
notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). To report a vulnerability, see the
[security policy](SECURITY.md).
