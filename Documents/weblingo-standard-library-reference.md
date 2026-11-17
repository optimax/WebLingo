# WebLingo Standard Library Reference

What every WebLingo program can use without writing it: the built-in functions, the built-in classes and their members,
the standard modules, and, in WASP's pages, the request and the response. The language itself, its syntax and its
statements, is the WebLingo Language Reference's (`Documents/weblingo-language-reference.md`); building sites is the
WASP User Manual's (`Documents/wasp-user-manual.md`).

This reference describes what is built. Every example here was run with `weblingo`, its output as shown. WebLingo is
pre-alpha: what is still to come is listed in the Language Reference's last section.

## Contents

1. [Built-in functions](#1-built-in-functions)
2. [`Object`](#2-object)
3. [`Boolean`, `Integer`, `Number` and `String`](#3-boolean-integer-number-and-string)
4. [`Array`, `Map` and `Set`](#4-array-map-and-set)
5. [`Iterator`](#5-iterator)
6. [`Function` and `Class`](#6-function-and-class)
7. [`Error` and the runtime errors](#7-error-and-the-runtime-errors)
8. [`Task` and `std:tasks`](#8-task-and-stdtasks)
9. [`Module`](#9-module)
10. [`Bytes` and `BytesBuilder`](#10-bytes-and-bytesbuilder)
11. [`DateTime`](#11-datetime)
12. [`std:fs`](#12-stdfs)
13. [Native modules](#13-native-modules)
14. [In WASP's pages](#14-in-wasps-pages)

---

## 1. Built-in functions

They are `std:system`'s, a module every file imports without saying so, so they are always in scope.

| Function | What it does |
|---|---|
| `write(value)` | writes a value's text, without a line break |
| `writeLn(value)` | writes a value's text and a line break |
| `delay(milliseconds)` | a task that completes, with `null`, after that time |

```javascript
write("Tea ");
writeLn("time");
writeLn([1, "two", null]);
await delay(10);
writeLn("ten milliseconds later");
```

```text
Tea time
[1, "two", null]
ten milliseconds later
```

`write` writes where its host says: the console under `weblingo`, the response's body in a page, where it is not
escaped. A value's text is what its `toString()` gives.

---

## 2. `Object`

The class every class extends, unless it extends another; every value but `null` has its members.

| Member | What it is |
|---|---|
| `x.equal(other)` | whether `x` equals `other`: `x == other` unless its class says otherwise |
| `x.toString()` | `x`'s text, what `write` writes |
| `x.__instanceOf` | `x`'s class |
| `C.__className` | a class's name, which assigning changes |
| `C.__parent` | the class a class extends, `null` for `Object` |
| `new Object()` | a plain instance, with no fields |

```javascript
class Point {
  constructor(x, y) { this.x = x; this.y = y; }
  equal(other) { return other is Point && this.x == other.x && this.y == other.y; }
  toString() { return $"({this.x}, {this.y})"; }
}
var p = new Point(1, 2);
writeLn([p.equal(new Point(1, 2)), p == new Point(1, 2)]);
writeLn(p);
writeLn([p.__instanceOf.__className, Point.__parent.__className]);
```

```text
[true, false]
(1, 2)
["Point", "Object"]
```

`==` compares instances by identity; `equal` is the comparison a class defines. A class's `toString()` is what `write`
and `<%= %>` write for its instances.

---

## 3. `Boolean`, `Integer`, `Number` and `String`

The classes of the simple values: `true` is a `Boolean`, `42` an `Integer`, a 64-bit integer, `2.5` a `Number`, a
binary64, and `"tea"` a `String`.

| Member | What it is |
|---|---|
| `Integer.parse(text)`, `Number.parse(text)`, `Boolean.parse(text)` | the value a string writes, as its literal does; WL3001 otherwise |
| `new Integer(value)`, `new Number(value)`, `new Boolean(value)`, `new String(value)` | the same value, or one parsed from a string; `0`, `0.0`, `false` and `""` without one |
| `s.length` | a string's length, in Unicode scalar values |
| `s[i]` | the string's `i`-th Unicode scalar value, as a string of one; `s?[i]` gives `null` outside it |
| `s.iterate()` | an `Iterator` of its characters, each a string of one |

```javascript
writeLn([Integer.parse("42") + 1, Number.parse("1.5") * 2, Boolean.parse("true")]);
writeLn([new Integer(), new Number(), new String(), new Boolean()]);
writeLn([12.toString(), "héllo".length, "héllo"[1]]);
foreach (var c in "tea") write(c + " ");
writeLn("");
```

```text
[43, 3.0, true]
[0, 0.0, "", false]
["12", 5, "é"]
t e a 
```

Strings are immutable: `s[i] = c` is an error. A class may extend `Integer`, `Number`, `Boolean` or `String`, its
instances holding a value of that kind (the Language Reference, "Classes of values").

---

## 4. `Array`, `Map` and `Set`

| Member | What it is |
|---|---|
| `a.length` | how many elements an array has |
| `a[i]`, `a[i] = v` | its element `i`, from 0; WL3005 outside it, `a?[i]` `null` there |
| `a.push(v)` | adds `v` at its end |
| `a.pop()` | removes its last element, and gives it; WL3005 when it is empty |
| `m.length` | how many entries a map has |
| `m[k]`, `m[k] = v` | its value of the key `k`, WL3007 when it has none, `m?[k]` `null` there; adds or replaces it |
| `m.has(k)` | whether it has the key `k` |
| `m.remove(k)` | removes the key `k`, giving whether it had it |
| `s.length` | how many values a set has |
| `s.add(v)` | adds `v`, giving whether it was new |
| `s.has(v)` | whether it has `v` |
| `s.remove(v)` | removes `v`, giving whether it had it |
| `x.iterate()` | an `Iterator` of its elements, its values, or its entries, each `[key, value]` |
| `new Array()`, `new Map()`, `new Set()` | an empty one, as `[]`, `{}` and `#{}` are |

```javascript
var a = [1, 2];
a.push(3);
writeLn([a.pop(), a, a.length]);
var m = { tea: 2.5 };
m["cake"] = 3.0;
writeLn([m.has("tea"), m.remove("tea"), m.has("tea"), m.length]);
var s = #{"green"};
writeLn([s.add("black"), s.add("green"), s.has("black"), s.remove("green"), s.length]);
foreach (var (name, price) in { scone: 2, bun: 1 }) writeLn($"{name}: {price}");
```

```text
[3, [1, 2], 2]
[true, true, false, 1]
[true, false, true, true, 1]
scone: 2
bun: 1
```

A map's keys, and a set's values, are strings, integers, Booleans and `null`; a map's and a set's order is not defined.
A class may extend `Array`, `Map` or `Set`.

---

## 5. `Iterator`

What `iterate()` gives, and what `foreach` asks for: a collection's, and any class's that has an `iterate()`.

| Member | What it is |
|---|---|
| `it.hasNext()` | whether it has another item |
| `it.next()` | its next item; WL3005 past the last |

```javascript
var it = ["a", "b"].iterate();
while (it.hasNext()) writeLn(it.next());
class Countdown {
  constructor(from) { this.from = from; }
  iterate() { return new Steps(this.from); }
}
class Steps {
  constructor(n) { this.n = n; }
  hasNext() { return this.n > 0; }
  next() { this.n = this.n - 1; return this.n + 1; }
}
foreach (var n in new Countdown(3)) write(n);
writeLn("");
```

```text
a
b
321
```

---

## 6. `Function` and `Class`

| Member | What it is |
|---|---|
| `new Function(f)` | `f` itself; `new Function()`, a function that does nothing and gives `null` |
| `new Class(name, base)` | a new class, its name and its base optional, `Object` without one |
| `C.setMethod(name, f)` | gives the class a method, a function whose first parameter is `this`; `"constructor"` its constructor |
| `C.removeMethod(name)` | removes the class's own method, giving whether it had it |
| `C.setProperty(name, getter, setter)` | gives it a property, each a function with `this`, or `null` for none |
| `C.setStaticProperty(name, getter, setter)` | gives it a static property, each a function without `this` |
| `C.setConstant(name, value)` | gives it a constant, read through the class and its instances |
| `C.name = f` | gives it a static member |

```javascript
var Greeter = new Class("Greeter");
Greeter.setMethod("constructor", function (this, name) { this.name = name; });
Greeter.setMethod("greet", function (this) { return "Hello, " + this.name; });
Greeter.setProperty("shout", function (this) { return this.name + "!"; }, null);
Greeter.setConstant("language", "English");
var g = new Greeter("Ann");
writeLn([g.greet(), g.shout, Greeter.language, Greeter.__className]);
Greeter.removeMethod("greet");
writeLn(g?.greet);
var double = new Function(x => x * 2);
writeLn(double(21));
```

```text
["Hello, Ann", "Ann!", "English", "Greeter"]
null
42
```

A class built so is a class as any declared one is: its instances are made by `new`, it may be extended, and `extend`
gives it methods too.

---

## 7. `Error` and the runtime errors

| Member | What it is |
|---|---|
| `new Error(message)` | an error, which a program throws |
| `e.message` | its message |
| `e.code` | its diagnostic code, `null` for one a program made |

A runtime error is an instance of a class extending `Error`, which a `catch` may name:

| Class | Code | When |
|---|---|---|
| `InitializationError` | WL2004 | a variable read before its declaration has run |
| `CallError` | WL2005 | calling what is not a function |
| `ArgumentError` | WL2006 | a call with the wrong number of arguments |
| `TypeError` | WL3001 | an operation on a value it does not take |
| `OverflowError` | WL3002 | an integer's result beyond 64 bits |
| `IndexError` | WL3005 | an index outside an array, a string or bytes |
| `MemberError` | WL3006 | a member a value does not have |
| `KeyError` | WL3007 | a key a map does not have |
| `TimeoutError` | WL3010 | a task that did not complete within `Task.timeout`'s time |
| `IOError` | WL3011 | a file or a folder that cannot be read, written or removed |
| `DivisionByZeroError` | WL3013 | an integer's remainder by 0, `7 % 0` |

```javascript
try {
  var empty = [];
  empty.pop();
} catch (IndexError e) {
  writeLn([e.code, e.message]);
}
class NotFound extends Error {
  constructor(what) { super($"{what} was not found."); this.what = what; }
}
try { throw new NotFound("Sencha"); } catch (NotFound e) { writeLn([e.message, e.code, e.what, e is Error]); }
```

```text
["WL3005", "'pop' cannot remove an element from an empty array."]
["Sencha was not found.", null, "Sencha", true]
```

Running out of the execution's budget (WL3003) and a cancellation (WL3004) stop a program, and no `catch` catches them.

---

## 8. `Task` and `std:tasks`

A call of an `async` function gives a task, a `Task`, which `await` waits for.

| Member | What it is |
|---|---|
| `Task.all(values)` | a task of an array of their results, in their order, once all have completed; it faults with the first fault |
| `Task.any(values)` | a task of the first of them to complete, or to fault |
| `Task.timeout(task, milliseconds)` | the task's result, or a `TimeoutError` if it takes longer; the task goes on |
| `t.forget()` | its fault, if it faults, reported nowhere |

The values may be tasks, or any others, which are their own results. `std:tasks` exports `all`, `any` and `timeout`,
the same: `import { all } from "std:tasks";`.

```javascript
import { all } from "std:tasks";
async function brew(tea, minutes) { await delay(minutes); return tea; }
writeLn(await all([brew("green", 3), brew("black", 1)]));
writeLn(await Task.any([brew("green", 3), brew("black", 1)]));
try {
  await Task.timeout(brew("oolong", 50), 5);
} catch (TimeoutError e) {
  writeLn(e.message);
}
var later = brew("white", 1);
later.forget();
writeLn(later is Task);
```

```text
["green", "black"]
black
The task did not complete within 5 milliseconds.
true
```

When a program ends, the first fault of a task nothing awaited stops it, unless the task was forgotten.

---

## 9. `Module`

| Member | What it is |
|---|---|
| `Module.import(path, values)` | a task of the module at `path`, compiled and run when it is called, its parameters given `values`, a map |

```javascript
var maths = await Module.import("09-maths.wl", { factor: 3 });
writeLn(maths.scale(14));
```

with `09-maths.wl`:

```javascript
param factor = 2;
export function scale(x) { return x * factor; }
```

```text
42
```

A module runs once in an execution for each set of values given it; its instance's members are its exports.

---

## 10. `Bytes` and `BytesBuilder`

Bytes are binary data, immutable, as a string is.

| Member | What it is |
|---|---|
| `Bytes.from(value, charset)` | bytes of a string, encoded, UTF-8 unless a charset is named; of an array of integers from 0 to 255; or bytes |
| `Bytes.concat(parts)` | bytes and integers from 0 to 255, joined |
| `Bytes.fixedTimeEqual(a, b)` | whether two bytes are equal, in a time that does not depend on where they differ |
| `b.length`, `b[i]` | how many bytes, and the byte at `i`, from 0 to 255 |
| `b.slice(start, end)` | the bytes from `start` to `end`, the end when it is left out |
| `b.text(charset)` | the bytes read as text, UTF-8 unless a charset is named |
| `new BytesBuilder()` | a builder, its `add(value)` taking bytes, an integer from 0 to 255 or a string, its `length`, and `toBytes()` |

The charsets are `utf-8`, `utf-16` (or `utf-16le`), `utf-16be`, `us-ascii` and `iso-8859-1` (or `latin1`), named
without regard to case.

```javascript
var b = Bytes.from("tea");
writeLn([b, b.length, b[0], b.slice(1).text()]);
var builder = new BytesBuilder();
builder.add("green ").add(Bytes.from("tea")).add(33);
writeLn(builder.toBytes().text());
writeLn(Bytes.fixedTimeEqual(Bytes.from("secret"), Bytes.from("secret")));
```

```text
[Bytes:[116, 101, 97], 3, 116, "ea"]
green tea!
true
```

---

## 11. `DateTime`

A date and time: an instant, to the millisecond, with its offset from UTC, immutable, in the years 1 to 9999.

| Member | What it is |
|---|---|
| `DateTime.now()` | the server's time, with the server's offset |
| `DateTime.utcNow()` | the same instant, in UTC |
| `DateTime.local(year, month, day, hour, minute, second, millisecond)` | a date in the server's time zone, those after the day optional, 0 when left out |
| `DateTime.utc(year, month, day, …)` | the same, in UTC |
| `DateTime.parse(text, format)` | a date read from ISO 8601's text, or from a format of .NET's; a time without a zone the server's |
| `DateTime.fromUnixMilliseconds(ms)` | the date `ms` milliseconds after 1970-01-01T00:00:00Z, in UTC |
| `d.year`, `d.month`, `d.day`, `d.hour`, `d.minute`, `d.second`, `d.millisecond` | its time on its own clocks, months and days from 1 |
| `d.dayOfWeek`, `d.dayOfYear` | 0 for Sunday to 6 for Saturday; the day of its year, from 1 |
| `d.offset`, `d.unixMilliseconds` | minutes ahead of UTC; milliseconds since 1970-01-01T00:00:00Z |
| `d.addYears(n)`, `d.addMonths(n)` | a date that many calendar years or months away, its day kept, or its month's last |
| `d.addDays(n)`, `d.addHours(n)`, `d.addMinutes(n)`, `d.addSeconds(n)`, `d.addMilliseconds(n)` | a date that much later, or earlier, a number rounded to the millisecond |
| `d.toUtc()`, `d.toLocal()` | the same instant in UTC, or in the server's time zone |
| `d.format(format)` | its text by a .NET date and time format, standard (`"o"`, `"d"`) or custom (`"d MMMM yyyy"`), in English |

`==`, `<` and the other comparisons compare instants; `b - a` is the milliseconds between them. `write` writes a date
as `DateTime:` and ISO 8601's text, and an interpolation formats one, `{d:yyyy-MM-dd}`.

```javascript
var opened = DateTime.utc(2026, 11, 16, 9);
writeLn([opened, opened.dayOfWeek, opened.addDays(7).format("d MMMM")]);
var closed = DateTime.parse("2026-11-16T17:30:00Z");
writeLn([(closed - opened) / 3600000, closed > opened]);
```

```text
[DateTime:2026-11-16T09:00:00Z, 1, "23 November"]
[8.5, true]
```

The time is the host's clock's, and the server's time zone the host's; WebLingo knows no other zones yet.

---

## 12. `std:fs`

Files and folders, through the platform's file system. Each function is asynchronous, giving a task, and has a
synchronous form whose name ends in `Sync`: `readText` and `readTextSync`.

| Function | What it does |
|---|---|
| `readText(path, encoding)` | a file's text |
| `writeText(path, text, encoding)` | writes a file, replacing it |
| `appendText(path, text, encoding)` | adds to a file's end, making it if it is not there |
| `exists(path)` | whether a file or a folder is there |
| `remove(path)` | removes a file |
| `list(path)` | the names of a folder's files and folders |
| `makeFolder(path)` | makes a folder, and the folders it is in |
| `removeFolder(path)` | removes a folder, which must be empty |
| `File.open(path, mode, encoding)` | a file opened, to read it, `"r"`, to write it, `"w"`, or to append to it, `"a"` |
| `file.read()` | the file's next line, without its line break, or `null` at its end |
| `file.write(text)` | writes text to it |
| `file.dispose()` | closes it, as `using` does |

Text is UTF-8 unless an encoding is named, as .NET names them: `utf-16`, `utf-16be`, `utf-32`, `us-ascii`,
`iso-8859-1`. A relative path is from the program's current folder. A file or a folder that cannot be read, written
or removed is an `IOError`, WL3011, its message naming the path as the script gave it.

```javascript
import fs from "std:fs";
await fs.makeFolder("notes");
await fs.writeText("notes/menu.txt", "green\n");
await fs.appendText("notes/menu.txt", "black\n");
writeLn(await fs.list("notes"));
using (var file = await fs.File.open("notes/menu.txt", "r")) {
  var line = await file.read();
  while (line != null) {
    writeLn("- " + line);
    line = await file.read();
  }
}
await fs.remove("notes/menu.txt");
await fs.removeFolder("notes");
writeLn(fs.existsSync("notes"));
try { fs.readTextSync("missing.txt"); } catch (IOError e) { writeLn([e.code, e.message]); }
```

```text
["menu.txt"]
- green
- black
false
["WL3011", "'missing.txt' cannot be read: there is no such file or folder."]
```

---

## 13. Native modules

A native module is code compiled for .NET that extends what scripts can use: functions and classes, imported as any
module's are, by a scheme naming a folder of them, `import db from "native:sqlite";`. The folders are named by the
configuration, `weblingo.config` beside a script and `wasp.config` in a site, `{ "schemes": { "native":
"C:/WebLingo/native" } }`, and by the command line's `--scheme native=folder`. The standard modules, `std:system`,
`std:tasks` and `std:fs`, are native modules themselves, in the `std` folder beside `weblingo` and `wasp`.

---

## 14. In WASP's pages

A page, a layout and a component that declare `param request;` and `param response;` are given the request they
answer and the response they make; a `.wl` route handler is given them as its function's arguments. Their members are
the WASP User Manual's, section 9; in brief:

| Member | What it is |
|---|---|
| `request.method`, `request.path`, `request.scheme`, `request.host` | the request's method, its path, decoded, and where it was sent |
| `request.query` | the query's names and values, a map |
| `request.headers` | its headers, read by name without regard to case |
| `await request.text()`, `await request.form()`, `await request.read()` | its body, as text, as a form's fields, or in pieces, as bytes |
| `response.status` | the response's status, 200 until set |
| `response.headers` | its headers, set by name, with `has`, `add` and `remove` |

A page also has `renderComponent(path, values)`, a task, which renders a component, and a layout `renderBody()`, which
renders its page's body where it stands (the WASP User Manual, sections 6 and 7).
