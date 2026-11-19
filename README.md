# WebLingo

WebLingo is a JavaScript-inspired scripting language for command-line programs and server-side web applications.
It aims to be a language with no surprises. Conversions are explicit, conditions require Booleans, and integer
overflow raises an error.

Write scripts and modules in `.wl` files. For web applications, **WASP** (Web Application Server Pages) lets you
embed WebLingo in HTML using `.wasp` templates, with automatic escaping for interpolated values based on where
they appear in the page. WebLingo is currently pre-alpha.

## Try it

With the .NET 10 SDK installed, run the included hello-world script from the repository root.

```sh
cd WebLingo
dotnet run --project src/WebLingo.Cli -- run samples/HelloWorld/hello.wl
```

## Learn more

- [Language reference](Documents/weblingo-language-reference.md) and [example scripts](Examples.wl/)
- [Standard library reference](Documents/weblingo-standard-library-reference.md)
- [WASP user manual](Documents/wasp-user-manual.md) and [example sites](Examples.wasp/)
- [Build and test instructions](WebLingo/README.md)
