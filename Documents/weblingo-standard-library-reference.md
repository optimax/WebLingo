# WebLingo Standard Library Reference

What every WebLingo program can use without writing it: the built-in functions, the built-in classes and their members,
the standard modules, and, in WASP's pages, the request, the response and their headers. The language itself, its syntax and its
statements, is the WebLingo Language Reference's (`Documents/weblingo-language-reference.md`); building sites is the
WASP User Manual's (`Documents/wasp-user-manual.md`).

This reference describes the standard library in the current release and shows what its examples write.

## Contents

1. [Built-in functions and `std:console`](#1-built-in-functions-and-stdconsole)
2. [`Object`](#2-object)
3. [`Boolean`, `Integer`, `Number` and `String`](#3-boolean-integer-number-and-string)
4. [`Array`, `Map` and `Set`](#4-array-map-and-set)
5. [`Iterator`](#5-iterator)
6. [`Function` and `Class`](#6-function-and-class)
7. [`Error` and the runtime errors](#7-error-and-the-runtime-errors)
8. [`Task` and `std:tasks`](#8-task-and-stdtasks)
9. [`Module`](#9-module)
10. [`Bytes`, `BytesBuilder` and `std:bytes`](#10-bytes-bytesbuilder-and-stdbytes)
11. [`DateTime` and `std:datetime`](#11-datetime-and-stddatetime)
12. [`Regex` and `std:regex`](#12-regex-and-stdregex)
13. [`Json` and `std:json`](#13-json-and-stdjson)
14. [`std:fs`](#14-stdfs)
15. [`std:http`](#15-stdhttp)
16. [`std:net`](#16-stdnet)
17. [`std:db`](#17-stddb)
18. [`std:crypto`](#18-stdcrypto)
19. [`std:env`](#19-stdenv)
20. [`std:math`](#20-stdmath)
21. [Native modules](#21-native-modules)
22. [In WASP's pages](#22-in-wasps-pages)
23. [`windows:com`](#23-windowscom)
24. [`std:path`](#24-stdpath)
25. [`std:url`](#25-stdurl)
26. [`std:csv`](#26-stdcsv)
27. [`std:compression`](#27-stdcompression)
28. [`std:random`](#28-stdrandom)
29. [`std:process`](#29-stdprocess)
30. [`std:test`](#30-stdtest)
31. [`std:mail`](#31-stdmail)
32. [`std:log`](#32-stdlog)
33. [`std:workers`](#33-stdworkers)
34. [`std:shared`](#34-stdshared)

---

## 1. Built-in functions and `std:console`

`delay` is `std:system`'s, which every program imports implicitly, so that it is in its scope without an import; an
import of it, `import { delay } from "std:system";`, is allowed, and means the same. The console's writing and reading
are `std:console`'s, which a program imports, `import { write, writeLn } from "std:console";`: a script that imports it
is a console program, and one that does not writes to no console. Nothing of another standard module is in a program
that does not import it: the classes of the kinds of values no
literal makes are their modules', `Bytes` and `BytesBuilder` `std:bytes`'s, `DateTime` `std:datetime`'s, `Regex`
`std:regex`'s, `Json` `std:json`'s and `Html` `std:html`'s, imported to name them, `import { DateTime } from
"std:datetime";`; a value of their kinds, which a module or a method gives, keeps its members without the import.

| `std:system`'s | What it does |
|---|---|
| `delay(milliseconds)` | a task that completes, with `null`, after that time |

| `std:console`'s | What it does |
|---|---|
| `write(value)` | writes a value's text to the console, without a line break |
| `writeLn(value)` | writes a value's text and a line break |
| `print(value)`, `printLn(value)` | `write` and `writeLn` by other names, which write as they do |
| `writeError(value)` | writes a value's text to the standard error, without a line break |
| `writeErrorLn(value)` | writes a value's text and a line break to the standard error |
| `readLine()` | a task of the console's next line, without its line break, `null` at the end of the input |
| `readLineSync()` | the same, at once, its worker waiting for the line |
| `prompt(text)` | writes the text, as `write` does, then a task of the next line, as `readLine()` |
| `readPassword()` | a task of the next line, not shown as it is typed where the input is a console's |

```javascript
import { write, writeLn } from "std:console"; import { delay } from "std:system";
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

`write` and `writeLn` write to a script's standard output under `weblingo`, and `writeError` and `writeErrorLn` to its
standard error, as .NET's `Console.Error` is written. A value's text is what its `toString()` gives. A site has no
console: under `wasp`, `std:console` cannot be imported, WL2015, as a module that is not installed cannot; a page's
text, its `<%= %>` and its blocks' own `write` and `writeLn`, which need no import, write its response, where they are
not escaped, and a `.wlg` route handler writes it with `response.write` and `response.writeLn` (section 22).

The console's reading reads a script's standard input under `weblingo`, typed, or piped from a file or another
program, `type data.txt | weblingo filter.wlg`, UTF-8; a host that gives a script no console makes each read an
`IOError`, WL3011, saying so. Given the lines `Anna`, `green tea` and `black tea`, piped:

```javascript
import { writeLn, readLine, prompt } from "std:console";
var name = await prompt("Your name: ");
writeLn($"Hello, {name}!");
var line = await readLine();
while (line != null) {
  writeLn(line.toUpper());
  line = await readLine();
}
```

```text
Your name: Hello, Anna!
GREEN TEA
BLACK TEA
```

### `Console` and `ConsoleColor`

`Console` controls the console, its members static, .NET's `Console`'s in lower camel case: its colours, its cursor,
its window and its buffer, its keys and Ctrl+C. `ConsoleColor`'s constants are the colours' names, so that a colour
read compares with either, `Console.foregroundColor == ConsoleColor.red`.

| Member | What it is |
|---|---|
| `Console.title` | the console window's title; reading it is Windows's alone |
| `Console.foregroundColor`, `Console.backgroundColor` | the colours text is written in, each a name, `"darkBlue"`, or a constant of `ConsoleColor`; another name WL3001 |
| `Console.resetColor()` | the colours as they were |
| `Console.cursorLeft`, `Console.cursorTop` | the cursor's column and row, from 0 |
| `Console.getCursorPosition()`, `Console.setCursorPosition(left, top)` | the cursor's position, a map `{ left, top }`, and its moving |
| `Console.cursorSize`, `Console.cursorVisible` | the cursor's height, a percentage of a cell, set on Windows alone, and whether it shows, read on Windows alone |
| `Console.windowWidth`, `Console.windowHeight`, `Console.windowLeft`, `Console.windowTop` | the window's size and position in the buffer, set on Windows alone |
| `Console.setWindowSize(width, height)`, `Console.setWindowPosition(left, top)` | both at once, Windows's alone |
| `Console.bufferWidth`, `Console.bufferHeight`, `Console.setBufferSize(width, height)` | the buffer's size, set on Windows alone |
| `Console.largestWindowWidth`, `Console.largestWindowHeight` | the largest window the screen has room for |
| `Console.moveBufferArea(sourceLeft, sourceTop, sourceWidth, sourceHeight, targetLeft, targetTop, sourceChar, sourceForeColor, sourceBackColor)` | copies an area of the buffer, the area filled with the character and its colours where they are given, Windows's alone |
| `Console.clear()`, `Console.beep()`, `Console.beep(frequency, duration)` | clears the buffer; a beep, of a frequency and a duration on Windows alone |
| `await Console.readKey(intercept)`, `Console.readKeySync(intercept)` | the next key typed, a map, `{ key: "enter", char: "\r", shift: false, alt: false, control: false }`, not shown where `intercept` is `true` |
| `await Console.read()`, `Console.readSync()` | the input's next character, a string of one, `null` at its end |
| `Console.keyAvailable`, `Console.capsLock`, `Console.numberLock` | whether a key waits to be read; Caps Lock and Num Lock, Windows's alone |
| `Console.treatControlCAsInput` | whether Ctrl+C is read as a key, rather than pressed |
| `Console.onCancelKeyPress(handler)` | Ctrl+C's and Ctrl+Break's handler, called with `"c"` or `"break"`, which keeps the program running by giving `true`, or none, `null` |
| `Console.isInputRedirected`, `Console.isOutputRedirected`, `Console.isErrorRedirected` | whether the input, the output and the errors are a file's or a pipe's rather than the console's |
| `Console.inputEncoding`, `Console.outputEncoding` | `"utf-8"`: a script's text is UTF-8 always |
| `Console.write(value)` … `Console.readPassword()` | `std:console`'s writing and reading, as members of `Console` too |

A member without a console, under a host that gives a script none, or that the console cannot do, its output a pipe's,
is an `IOError`, WL3011; one the platform has not, Windows's alone elsewhere, a `PlatformError`, WL3022; a value it does
not take, a cursor beyond the buffer, WL3001.

```javascript
import { Console, ConsoleColor, writeLn } from "std:console";
Console.foregroundColor = ConsoleColor.yellow;
writeLn("in yellow");
Console.resetColor();
Console.onCancelKeyPress(key => {
  writeLn($"Ctrl+{key} pressed; running on");
  return true;
});
writeLn([Console.foregroundColor == ConsoleColor.gray, Console.outputEncoding]);
```

```text
in yellow
[true, "utf-8"]
```

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
import { writeLn } from "std:console";
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
| `Decimal.parse(text)`, `new Decimal(value)` | a decimal, `1.5m`, read from its digits, or made from an integer, exactly, or a number, converted |
| `new Number(d)`, `new Integer(d)` | a decimal converted to a number, or to an integer when it is whole |
| `s.compare(other)` | -1, 0 or 1, as `s` comes before `other`, equals it or comes after it, by their Unicode values, as `<` orders them |
| `s.indexOf(search, from)`, `s.lastIndexOf(search)` | where `search` first starts, at `from` or after it, `from` optional, or where it last starts; -1 if nowhere |
| `s.contains(search)` or `s.includes(…)`, `s.startsWith(prefix)`, `s.endsWith(suffix)` | whether `s` has it, begins with it, ends with it |
| `s.slice(start, end)` | the characters from `start` to before `end`, `end` optional; an error outside the string |
| `s.split(separator)` | an array of the parts between separators, empty ones included; each character for `""` |
| `s.trim()`, `s.trimStart()`, `s.trimEnd()` | `s` without the white space at its ends, its start or its end |
| `s.trim(chars)`, `s.trimStart(chars)`, `s.trimEnd(chars)` | `s` without any of the characters `chars` holds, each a Unicode scalar value, as many as stand at its ends, its start or its end: `"a//".trimEnd("/")` is `"a"`, `"x;,;".trimEnd(";,")` is `"x"`; `""` trims nothing |
| `s.removePrefix(text)`, `s.removeSuffix(text)` | `s` without `text`, once, where `s` begins, or ends, with it, else `s` as it is: `"report.txt".removeSuffix(".txt")` is `"report"` |
| `s.toUpper()` or `s.toUpperCase()`, `s.toLower()` or `s.toLowerCase()` | `s` in capitals, or small letters, the same on every server |
| `s.replace(old, new)` or `s.replaceAll(…)` | `s` with every `old` replaced by `new` |
| `s.repeat(n)` | `s` `n` times over |
| `s.padStart(width, pad)` or `s.padLeft(…)`, `s.padEnd(width, pad)` or `s.padRight(…)` | `s` padded to `width` characters, with spaces unless `pad` is given |
| `separator.join(array)` | the array's strings, numbers, Booleans, decimals and dates, `null` as nothing, with `separator` between them |

`trim`'s argument is a set of characters, not a text: to remove a suffix, use `removeSuffix`. `"report.txt".trimEnd(".txt")`
is `"repor"`, every `.`, `t` and `x` at the end trimmed, as Python's `"report.txt".rstrip(".txt")` is, a trap that
`removePrefix` and `removeSuffix`, separate names, keep out.

```javascript
import { write, writeLn } from "std:console";
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
| `a[i]`, `a[i] = v` | reads or replaces its element `i`, counting from 0 or from the end with a negative index. `-1` is the last element. WL3005 outside the array, or `null` from `a?[i]` |
| `a.push(v)` | adds `v` at its end |
| `a.pop()` | removes its last element, and gives it; WL3005 when it is empty |
| `a.insert(i, v)`, `a.removeAt(i)` | puts `v` at `i`, from 0 to its length; removes the element at `i`, and gives it |
| `a.indexOf(v)`, `a.contains(v)` or `a.includes(v)` | where `v` first is, by `==`, or -1; whether it is there |
| `a.slice(start, end)`, `a.concat(other)` | a new array of its elements from `start` to before `end`, `end` optional; of its elements, then `other`'s |
| `a.join(separator)` | its strings, numbers, Booleans, decimals and dates, `null` as nothing, with `separator`, a comma unless given, between them |
| `a.reverse()`, `a.sort(compare)` | a new array, its elements the other way round, or in order: numbers by value, strings as `<` orders them, or by `compare(x, y)`, negative when `x` comes first |
| `a.map(f)` or `a.select(f)`, `a.filter(f)` or `a.where(f)` | a new array of `f(x)` for each element, or of the elements `f` gives `true` for |
| `a.reduce(f, initial)` | `f(total, x)` for each element in turn, starting from `initial` |
| `a.find(f)`, `a.any(f)` or `a.some(f)`, `a.all(f)` or `a.every(f)` | the first element `f` gives `true` for, or `null`; whether it gives `true` for one; for all |
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

Array methods that take a callback check it before visiting any elements. For example, `[].map(1)` raises
WL3001 with the message `The argument of 'map' must be a function, not the integer 1.` For `sort`, an
omitted or `null` comparator selects the default ordering.

```javascript
import { writeLn } from "std:console";
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
import { write, writeLn } from "std:console";
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
import { writeLn } from "std:console";
var Greeter = new Class("Greeter");
Greeter.setMethod("constructor", function (this, name) { this.name = name; });
Greeter.setMethod("greet", function (this) { return "Hello, " + this.name; });
Greeter.setProperty("shout", function (this) { return this.name + "!"; }, null);
Greeter.setConstant("language", "English");
var g = new Greeter("Anna");
writeLn([g.greet(), g.shout, Greeter.language, Greeter.__className]);
Greeter.removeMethod("greet");
writeLn(g?.greet);
var double = new Function(x => x * 2);
writeLn(double(21));
```

```text
["Hello, Anna", "Anna!", "English", "Greeter"]
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
| `NetworkError` | WL3014 | a host that cannot be reached, a connection that fails, a request that takes too long |
| `DatabaseError` | WL3015 | a statement a database refuses, a database that cannot be opened |
| `FilePermissionError` | WL3016 | a file or a folder a site's scripts may not reach, outside the folders its `wasp.config` allows |
| `ComError` | WL3012 | a COM object's failure, with its `number`, `source` and `description` (section 23) |
| `NetworkPermissionError` | WL3017 | a host the configuration's `"network"` does not let a script reach |
| `ComPermissionError` | WL3018 | a COM object the configuration's `"com"` does not let a script make (section 23) |
| `EnvironmentPermissionError` | WL3019 | a variable a site's scripts try to set, under `wasp`, which lets them read only (section 19) |
| `DeadlockError` | WL3020 | a program stuck awaiting a task `new Task()` made, nothing else able to run, thrown at that `await`, naming where the task was made and where it is awaited |
| `PlatformError` | WL3022 | a member of `Console` the platform has not, Windows's alone used elsewhere (section 1) |
| `MailError` | WL3023 | mail that cannot be sent, a server that cannot be reached or refuses it, with its `reply`, the server's reply code (section 31) |
| `SessionLockError` | WL3024 | a WASP page's session read after its request waited too long for it, another request of the session having it (section 22) |
| `SharedMemoryError` | WL3025 | a value `std:shared`'s `set` or `add` would keep past the store's most (section 34) |

```javascript
import { writeLn } from "std:console";
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
| `new Task()`, `t.complete(value)`, `t.fail(error)` | a task its program completes by hand, whoever holds it; `complete` and `fail` give whether they completed it, a task completing once, and are an error on a task `new` did not make |

The values may be tasks, or any others, which are their own results. `std:tasks` exports `all`, `any` and `timeout`,
the same: `import { all } from "std:tasks";`.

```javascript
import { writeLn } from "std:console"; import { delay } from "std:system";
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

A class may extend `Task`, its instances then tasks completed by hand, its own members first:

```javascript
import { writeLn } from "std:console";
class Job extends Task {
  constructor(name) { super(); this.name = name; }
  finish(value) { return this.complete($"{this.name}: {value}"); }
}
var job = new Job("report");
async function later() { await delay(5); job.finish(42); }
later();
writeLn(await job);
writeLn([job is Task, job]);
```

```text
report: 42
[true, Job:task]
```

---

## 9. `Module`

| Member | What it is |
|---|---|
| `Module.import(path, values)` | a task of the module at `path`, compiled and run when it is called, its parameters given `values`, a map |
| `new Module(name)` | a module defined in code rather than in a file, empty, of that name |
| `m.exec()` | a module defined in code's body, a task, which a class extending `Module` may override; `Module`'s gives `null` |

```javascript
import { writeLn } from "std:console";
var maths = await Module.import("09-maths.wlg", { factor: 3 });
writeLn(maths.scale(14));
```

with `09-maths.wlg`:

```javascript
param factor = 2;
export function scale(x) { return x * factor; }
```

```text
42
```

A module runs once in an execution for each set of values given it; its instance's members are its exports.

A class may extend `Module`, its instances then being modules defined in code, each named by its constructor's
`super(name)`, their members their fields and their class's methods:

```javascript
import { writeLn } from "std:console";
class Counter extends Module {
  constructor(name) { super(name); this.count = 0; }
  next() { this.count += 1; return this.count; }
  async exec() { return this.next() * 10; }
}
var tally = new Counter("tally");
tally.next();
writeLn(await tally.exec());
writeLn([tally, tally is Module]);
```

```text
20
[Counter:module tally, true]
```

Such a module has no path, so that no `import` finds it.

---

## 10. `Bytes`, `BytesBuilder` and `std:bytes`

Bytes are binary data, immutable, as a string is. Their class, `Bytes`, and `BytesBuilder` are `std:bytes`'s, which a
program imports to name them, `import { Bytes, BytesBuilder } from "std:bytes";`; bytes a module or a method gives keep
their members without the import.

| Member | What it is |
|---|---|
| `Bytes.from(value, charset)` | bytes of a string, encoded, UTF-8 unless a charset is named; of an array of integers from 0 to 255; or bytes |
| `new Bytes(value, charset)` | the same |
| `Bytes.concat(parts)` | bytes and integers from 0 to 255, joined |
| `Bytes.fixedTimeEqual(a, b)` | whether two bytes are equal, in a time that does not depend on where they differ |
| `Bytes.fromHex(text)` | the bytes hexadecimal writes, two digits for each, in either case |
| `Bytes.fromBase64(text, urlSafe)` | the bytes Base64 writes, padded with `=`; given `true`, in its URL-safe alphabet, padded or not |
| `b.length`, `b[i]` | how many bytes, and the byte at `i`, from 0 to 255 |
| `b.slice(start, end)` | the bytes from `start` to `end`, the end when it is left out |
| `b.text(charset)` | the bytes read as text, UTF-8 unless a charset is named |
| `b.toHex()` | the bytes as hexadecimal, two lower-case digits for each, `"746561"` |
| `b.toBase64(urlSafe)` | the bytes as Base64, RFC 4648's, padded with `=`; given `true`, its URL-safe alphabet, `-` and `_` for `+` and `/`, unpadded |
| `new BytesBuilder()` | a builder, its `add(value)` taking bytes, an integer from 0 to 255 or a string, its `length`, and `toBytes()`, which no later `add` changes; written as its bytes so far, `BytesBuilder:[104, 105]` |

The charsets are `utf-8`, `utf-16` (or `utf-16le`), `utf-16be`, `us-ascii` and `iso-8859-1` (or `latin1`), named
without regard to case. Text that is not hexadecimal, or not Base64, is WL3001.

A class may extend `Bytes` or `BytesBuilder`, its instances then holding bytes, or a builder, which `new` makes as
`Bytes.from(…)` and `new BytesBuilder()` make them, with their members and the class's own:

```javascript
import { writeLn } from "std:console";
import { BytesBuilder } from "std:bytes";
class Lines extends BytesBuilder {
  line(text) { this.add(text).add(10); return this; }
}
var lines = new Lines();
lines.line("green").line("tea");
writeLn(lines.toBytes().text());
writeLn(lines.length);
```

```text
green
tea

10
```

```javascript
import { writeLn } from "std:console";
import { Bytes, BytesBuilder } from "std:bytes";
var b = Bytes.from("tea");
writeLn([b, b.length, b[0], b.slice(1).text()]);
var builder = new BytesBuilder();
builder.add("green ").add(Bytes.from("tea")).add(33);
writeLn(builder.toBytes().text());
writeLn(Bytes.fixedTimeEqual(Bytes.from("secret"), Bytes.from("secret")));
writeLn([b.toHex(), b.toBase64(), Bytes.fromHex("746561").text()]);
```

```text
[Bytes:[116, 101, 97], 3, 116, "ea"]
green tea!
true
["746561", "dGVh", "tea"]
```

---

## 11. `DateTime` and `std:datetime`

A date and time: an instant, to the millisecond, with its offset from UTC, immutable, in the years 1 to 9999. Its class is
`std:datetime`'s, which a program imports to name it, `import { DateTime } from "std:datetime";`; a date a module or a
method gives keeps its members without the import.

| Member | What it is |
|---|---|
| `DateTime.now()` | the server's time, with the server's offset |
| `DateTime.utcNow()` | the same instant, in UTC |
| `DateTime.local(year, month, day, hour, minute, second, millisecond)` | a date in the server's time zone, those after the day optional, 0 when left out |
| `DateTime.utc(year, month, day, …)` | the same, in UTC |
| `new DateTime(year, month, day, …)` | the same as `DateTime.utc(…)`; a class extending `DateTime` holds the date it makes so |
| `DateTime.inZone(name, year, month, day, …)` | the same, in the zone of that IANA name, `"Europe/Warsaw"` |
| `DateTime.parse(text, format)` | a date read from ISO 8601's text, or from a format of .NET's; a time without a zone the server's |
| `DateTime.fromUnixMilliseconds(ms)` | the date `ms` milliseconds after 1970-01-01T00:00:00Z, in UTC |
| `d.year`, `d.month`, `d.day`, `d.hour`, `d.minute`, `d.second`, `d.millisecond` | its time on its own clocks, months and days from 1 |
| `d.dayOfWeek`, `d.dayOfYear` | 0 for Sunday to 6 for Saturday; the day of its year, from 1 |
| `d.offset`, `d.unixMilliseconds` | minutes ahead of UTC; milliseconds since 1970-01-01T00:00:00Z |
| `d.addYears(n)`, `d.addMonths(n)` | a date that many calendar years or months away, its day kept, or its month's last |
| `d.addDays(n)`, `d.addHours(n)`, `d.addMinutes(n)`, `d.addSeconds(n)`, `d.addMilliseconds(n)` | a date that much later, or earlier, a number rounded to the millisecond |
| `d.toUtc()`, `d.toLocal()` | the same instant in UTC, or in the server's time zone |
| `d.toZone(name)` | the same instant in the zone of that IANA name, `"Asia/Kolkata"` |
| `d.format(format)` | its text by a .NET date and time format, standard (`"o"`, `"d"`) or custom (`"d MMMM yyyy"`), in English |

`==`, `<` and the other comparisons compare instants; `b - a` is the milliseconds between them. `write` writes a date
as `DateTime:` and ISO 8601's text, and an interpolation formats one, `{d:yyyy-MM-dd}`.

```javascript
import { writeLn } from "std:console";
import { DateTime } from "std:datetime";
var opened = DateTime.utc(2026, 11, 16, 9);
writeLn([opened, opened.dayOfWeek, opened.addDays(7).format("d MMMM")]);
var closed = DateTime.parse("2026-11-16T17:30:00Z");
writeLn([(closed - opened) / 3600000, closed > opened]);
```

```text
[DateTime:2026-11-16T09:00:00Z, 1, "23 November"]
[8.5, true]
```

The time is the host's clock's, and the time zones, the server's and the others, the host's: those the server knows,
which may differ from one server to another, a name it does not know an error.

---

## 12. `Regex` and `std:regex`

A regular expression: a pattern in .NET's syntax, and flags, immutable. Its class is `std:regex`'s, which a program
imports to name it, `import { Regex } from "std:regex";`.

| Member | What it is |
|---|---|
| `new Regex(pattern, flags)` | a regular expression; the flags, a string, `i`, `m`, `s`, `x` and `n`, none unless given |
| `r.test(s)` or `r.isMatch(s)` | whether `s` has a match |
| `r.match(s)` or `r.exec(s)` | the first match, an array of its groups' text, the whole match first, `null` for a group that matched nothing; `null` for none |
| `r.matchAll(s)` or `r.matches(s)` | an array of every match, each as `match` gives it |
| `r.replace(s, replacement)`, `s.replace(r, replacement)` | `s` with every match replaced, `$1` the first group, `${name}` a named one, `$0` the whole match, `$$` a `$` |
| `r.split(s)`, `s.split(r)` | the parts of `s` between matches, the groups a match captured among them |
| `s.match(r)`, `s.matchAll(r)` | as `r.match(s)` and `r.matchAll(s)` |
| `r.pattern`, `r.flags` | the pattern and the flags it was made of |

A match that exceeds its time limit stops with a `TimeoutError` (WL3010). The default is one second. Use
`weblingo run --timeout 300 script.wlg` or `wasp run --timeout 300 page.wasp` to set it in milliseconds for that run. `write`
writes a regular expression as `Regex:/pattern/flags`.

---

## 13. `Json` and `std:json`

JSON's text, read and written by the class `Json`, which a program has by importing it, `import { Json } from "std:json";`.

| Member | What it is |
|---|---|
| `Json.parse(text)` | the value JSON's text writes: an object a map, an array an array, a number an integer when it has no fraction or exponent and fits 64 bits, else a number; WL3001 for text that is not JSON |
| `Json.stringify(value, indent)` | the value's JSON text, `indent` 0 to 10 spaces, 0 unless given: maps, arrays, sets, strings, numbers, decimals, Booleans, `null`, dates as their ISO text, and instances as their fields, or as their class's `toJson()` gives |

```javascript
import { writeLn } from "std:console";
import { Json } from "std:json";
var data = Json.parse("[1, 2.5, \"three\", {\"four\": null}]");
writeLn([data, data[0] is Integer, data[1] is Number]);
writeLn(Json.stringify(data));
```

```text
[[1, 2.5, "three", {"four": null}], true, true]
[1,2.5,"three",{"four":null}]
```

---

## 14. `std:fs`

Files and folders, through the platform's file system. Each function is asynchronous, giving a task, and has a
synchronous form whose name ends in `Sync`: `readText` and `readTextSync`. A file being read may be written, replaced or
removed by another program meanwhile, on Windows as on Linux: a read never keeps another program from changing it.

| Function | What it does |
|---|---|
| `readText(path, encoding)` | a file's text |
| `writeText(path, text, encoding)` | writes a file, replacing it |
| `appendText(path, text, encoding)` | adds to a file's end, making it if it is not there |
| `readBytes(path)` | a file's bytes, as `Bytes`, as they are |
| `writeBytes(path, bytes)` | writes a file's bytes, replacing it |
| `appendBytes(path, bytes)` | adds bytes to a file's end, making it if it is not there |
| `exists(path)` | whether a file or a folder is there |
| `isFolder(path)` | whether a folder is there; a file is not one |
| `copy(source, target)` | copies a file as it is, replacing one at `target`; a folder is not copied |
| `remove(path)` | removes a file |
| `list(path)` | the names of a folder's files and folders |
| `makeFolder(path)` | makes a folder, and the folders it is in |
| `removeFolder(path)` | removes a folder, which must be empty |
| `File.open(path, mode, encoding)` | a file opened for its text, to read it, `"r"`, to write it, `"w"`, or to append to it, `"a"`; or for its bytes, to read them, `"rb"`, to write them, `"wb"`, or to append them, `"ab"`, without an encoding |
| `file.read()` | a file opened for its text: its next line, without its line break, or `null` at its end |
| `file.read(count)` | a file opened for its bytes: its next bytes, as many as `count`, at most 16 MiB, fewer at its end, or `null` there |
| `file.write(value)` | writes text to a file opened for its text, or bytes to one opened for its bytes |
| `file.dispose()` | closes it, as `using` does |
| `new File()`, `file.open(path, mode, encoding)` | a file made, not yet open, then opened, a task giving the file, `openSync` at once; opened once, and not after its disposal; `File.open(…)` and `File.openSync(…)` are shorthand for both, making an instance of the class they are called on |

Text is UTF-8 unless an encoding is named, as .NET names them: `utf-16`, `utf-16be`, `utf-32`, `us-ascii`,
`iso-8859-1`. A relative path is from the program's current folder. A file or a folder that cannot be read, written
or removed is an `IOError`, WL3011, its message naming the path as the script gave it. A site's pages and handlers,
under `wasp`, reach only the folders its `wasp.config`'s `"files"` lets them read, or read and write, a relative path
from the site's root, and a path outside them is a `FilePermissionError`, WL3016 (the WASP User Manual, "Configuration").

```javascript
import { writeLn } from "std:console";
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

Bytes are read and written as they are, never as text, so that a script copies any file, an image or a program:

```javascript
import { writeLn } from "std:console";
import { Bytes } from "std:bytes";
import fs from "std:fs";
await fs.writeBytes("logo.bin", Bytes.from([137, 80, 78, 71, 0, 255]));
await fs.copy("logo.bin", "copy.bin");
writeLn([fs.isFolderSync("copy.bin"), fs.isFolderSync("."), (await fs.readBytes("copy.bin")).length]);
using (var file = await fs.File.open("copy.bin", "rb")) {
  var part = await file.read(4);
  while (part != null) {
    writeLn(part);
    part = await file.read(4);
  }
}
try { fs.copySync("missing.bin", "copy.bin"); } catch (IOError e) { writeLn(e.message); }
await fs.remove("logo.bin");
await fs.remove("copy.bin");
```

```text
[false, true, 6]
Bytes:[137, 80, 78, 71]
Bytes:[0, 255]
'missing.bin' cannot be copied to 'copy.bin': there is no such file or folder.
```

A class may extend `File`, `Socket` or `Database`, its own constructor synchronous, and never blocking on I/O, its
instances made, then opened; their class's factory, called on it, makes its instance, its constructor given no arguments:

```javascript
import { writeLn } from "std:console";
import fs from "std:fs";
class LogFile extends fs.File {
  constructor(prefix = "[log]") { super(); this.prefix = prefix; }
  log(line) { this.writeSync($"{this.prefix} {line}\n"); }
}
var log = new LogFile("[app]");
await log.open("app.log", "w");
log.log("started");
log.dispose();
var again = await LogFile.open("app.log", "r");
writeLn([again is LogFile, again.prefix, again.readSync()]);
again.dispose();
```

```text
[true, "[log]", "[app] started"]
```

---

## 15. `std:http`

HTTP requests to other servers. Each function is asynchronous, a task of the response, and its request is made off the worker, so that a page's other tasks go on while it waits.

| Function | What it does |
|---|---|
| `get(url, headers)` | a `GET` of `url` |
| `post(url, body, headers)` | a `POST` of `body` to `url` |
| `request(method, url, body, headers)` | a request of any method, `"PUT"`, `"DELETE"`, `"HEAD"`, its name sent in upper case |

`headers` and `body` may be left out: headers are a map of names to strings, `{ "Accept": "application/json" }`, and a body a string, sent as UTF-8, or bytes. A response is a map:

| Key | What it holds |
|---|---|
| `"status"` | its status, an integer, `200` |
| `"headers"` | its headers, each name in lower case, `"content-type"`, with its value, a header sent more than once with its values joined by `, ` |
| `"body"` | its body as text, in the charset its `Content-Type` names, UTF-8 unless it names another |
| `"bytes"` | its body as bytes |

A status that is not a success, `404`, is a response as any other. A redirect is given as it is, its status `301` or `302` and its `"location"`, not followed, so that a script reaches only the hosts it names. A request waits at most 30 seconds to connect and 100 seconds in all, and a body is at most 64 MB. An address that is not an `http` or `https` one is WL3001, and a host the script may not reach a `NetworkPermissionError`, WL3017; a host that cannot be reached, a connection that fails and a request that takes too long are a `NetworkError`, WL3014. A script `weblingo` runs may reach any host, and a site's only those its `wasp.config` allows, `{ "network": { "allow": ["api.example.com"] } }`.

Given a server on this machine, at port 8080, that has `teas.json`, `[{"name": "Sencha", "price": 4.5}, {"name": "Genmaicha", "price": 3.75}]`, and `menu.txt`, but no `green.json`:

```javascript
import { writeLn } from "std:console";
import http from "std:http";
import { Json } from "std:json";
var r = await http.get("http://localhost:8080/teas.json", { "Accept": "application/json" });
writeLn(r["status"]);
writeLn(r["headers"]["content-type"]);
var teas = Json.parse(r["body"]);
writeLn(teas[0]["name"]);
writeLn(r["bytes"].length);
var missing = await http.get("http://localhost:8080/green.json");
writeLn(missing["status"]);
var head = await http.request("HEAD", "http://localhost:8080/menu.txt");
writeLn([head["status"], head["body"]]);
try { await http.get("http://localhost:1/"); } catch (NetworkError e) { writeLn(e.code); }
```

```text
200
application/json
Sencha
72
404
[200, ""]
WL3014
```

---

## 16. `std:net`

TCP connections to other servers, through the native class `Socket`. Each operation but `dispose()` is asynchronous, a task, and runs off the worker.

| Member | What it does |
|---|---|
| `Socket.connect(host, port)` | a connection to `host`, a name or an address, at `port`, from 1 to 65535 |
| `socket.read()` | the bytes that have come, at least one and at most 64 KB, waiting for them, or `null` once the other end has closed |
| `socket.write(data)` | sends `data`, a string, as UTF-8, or bytes |
| `socket.dispose()` | closes it, as `using` does |
| `new Socket()`, `socket.connect(host, port)` | a socket made, not yet connected, then connected, a task giving the socket; `Socket.connect(…)` is shorthand for both, making an instance of the class it is called on |

A host the script may not reach is a `NetworkPermissionError`, WL3017, as for `std:http`; a host that cannot be reached, and a connection that fails, are a `NetworkError`, WL3014. Given the server above:

```javascript
import { writeLn } from "std:console";
import { Socket } from "std:net";
using (var socket = await Socket.connect("localhost", 8080)) {
  await socket.write("HEAD /menu.txt HTTP/1.0\r\nHost: localhost\r\n\r\n");
  var reply = "";
  var piece = await socket.read();
  while (piece != null) {
    reply += piece.text();
    piece = await socket.read();
  }
  writeLn(reply.split("\r\n")[0]);
}
```

```text
HTTP/1.0 200 OK
```

---

## 17. `std:db`

Databases, through the platform's: one module for every database, the SQL passed on as it is, so that each database's own SQL works. Its class, `Database`:

| Member | What it does |
|---|---|
| `Database.open(name)` | a task of a connection to the database the configuration names, or to the one a connection string describes, `"sqlite:C:/data/fish.db"` |
| `db.query(sql, values, options)` | a task of its rows, an array of maps, each from a column's name to its value |
| `db.rows(sql, values)` | a task returning a disposable cursor for reading one row at a time. The values map is optional |
| `db.execute(sql, values, options)` | a task of the number of rows the statement changed |
| `db.transaction(work, options)` | begins a transaction, awaits the function `work`, and commits, or rolls back where `work` throws, throwing on; a task of what `work` gave |
| `db.begin(options)`, `db.commit()`, `db.rollback()` | a transaction's steps, each a task; a connection's transactions do not nest |
| `db.provider`, `db.serverVersion` | the database's kind, `"sqlite"`, `"sqlserver"`, `"mysql"` or `"postgresql"`, and its version |
| `db.dispose()` | closes it, as `using` does, rolling back a transaction still open |
| `new Database(name)`, `db.open()` | a database made, its name kept, not yet open, then opened, a task giving the database; `Database.open(name)` is shorthand for both, making an instance of the class it is called on |

MySQL uses MySqlConnector connection strings such as `mysql:Server=localhost;Database=shop;User ID=app`.
PostgreSQL uses Npgsql connection strings such as `postgresql:Host=localhost;Database=shop;Username=app`.
For MySQL or PostgreSQL in WASP, put the connection string in `wasp.config` and open the database by name.
CLI scripts can also pass either connection string directly to `Database.open`.
PostgreSQL receives WebLingo date parameters as UTC instants in `timestamptz`.
MySQL receives them as their time on the server's clocks, as its `DATE` and `DATETIME` columns, which hold no offset, are read back.
PostgreSQL folds a name not quoted to lower case, so that `CREATE TABLE Items (Id …)` gives rows whose column is `id`.

Read each row with `await cursor.next()`. It returns a map with the same columns and values as `query`.
The first `null` marks exhaustion, and later reads also give `null`. Use `using` to close the cursor if you leave
the loop early. Exhaust or dispose the cursor before issuing another statement or changing the transaction on
the same connection. Read the cursor in order by awaiting each call to `next()` before starting another.

```javascript
import { writeLn } from "std:console";
import { Database } from "std:db";

using (var db = await Database.open("sqlite::memory:")) {
    await db.execute("CREATE TABLE Fish (Id INTEGER PRIMARY KEY, Name TEXT, Price INTEGER)");
    await db.execute("INSERT INTO Fish VALUES (1, 'Neon tetra', 3), (2, 'Guppy', 5), (3, 'Clownfish', 25)");
    using (var cursor = await db.rows("SELECT Name FROM Fish WHERE Price < @max ORDER BY Id", { max: 10 })) {
        var row = await cursor.next();
        while (row != null) {
            writeLn(row["Name"]);
            row = await cursor.next();
        }
    }
}
```

```text
Neon tetra
Guppy
```

A configuration names a script's or a site's databases, `weblingo.config` beside a script and a site's `wasp.config`, `{ "databases": { "fish": "sqlite:../database/fish.db" } }`, a relative path there being from the configuration's folder, and a script opens one by its name, `Database.open("fish")`, or by a connection string of its own, a relative path then from the current folder. SQLite's connection string is a file's path, the file made when it is not there, `:memory:`, a database in memory, or SQLite's own, which holds an `=`, `"sqlite:Data Source=fish.db;Mode=ReadOnly"`. SQL Server's is SqlClient's own, `"sqlserver:Server=localhost;Database=Todo;Integrated Security=true;TrustServerCertificate=true"`, `TrustServerCertificate` accepting the certificate a server made for itself when it was installed; it reaches SQL Server through SqlClient's managed network layer, on Windows as elsewhere, by TCP/IP, which the server must have enabled, as SQL Server's Developer and Express editions do not when they are installed, and never by its shared memory.

A statement's values are a map, `{ max: 10 }`, each named `@max` in its SQL, never written into its text: `null`, Booleans, integers, numbers, decimals, strings, bytes and dates. Its options are a map, a statement's `{ timeout: 30 }`, in seconds, and a transaction's `{ isolation: "serializable" }`, one of `readUncommitted`, `readCommitted`, `repeatableRead`, `serializable` and `snapshot`, one the database does not take an error. SQLite's values come back as integers, numbers, strings and bytes, a column declared a decimal, `DECIMAL(10, 2)`, as a decimal, a Boolean, `BOOLEAN`, as one, and a date, `DATETIME`, as one. SQL Server's come back as their types say: its integers, `TINYINT` to `BIGINT`, as integers, `FLOAT` and `REAL` as numbers, `DECIMAL` and `MONEY` as decimals, `BIT` as a Boolean, text as strings, `VARBINARY` as bytes, `DATETIMEOFFSET` as a date with its offset, a date without one, `DATE` or `DATETIME2`, as one on the server's clocks, and a `UNIQUEIDENTIFIER` or a `TIME` as its text. MySQL's and PostgreSQL's come back as their types say too: integers as integers, `DOUBLE` and `DOUBLE PRECISION` as numbers, `DECIMAL` and `NUMERIC` as decimals, `BOOLEAN` as a Boolean, text as strings, `BLOB` and `BYTEA` as bytes, PostgreSQL's `TIMESTAMPTZ` as a date in UTC, and a date without an offset, MySQL's `DATE` and `DATETIME` and PostgreSQL's `DATE`, as one on the server's clocks. A connection's operations run one at a time, and those a script starts without awaiting them, as `Task.all` does, in no defined order: a script awaits each one whose order matters. What cannot be asked for, a name no configuration gives, a value of another kind, an option it does not take, is WL3001; the database's own failure, a statement it refuses or a file it cannot open, a `DatabaseError`, WL3015, its message the database's, its line breaks LF on every platform.

```javascript
import { writeLn } from "std:console";
import { Database } from "std:db";
using (var db = await Database.open("sqlite::memory:")) {
  await db.execute("CREATE TABLE Fish (Id INTEGER PRIMARY KEY, Name TEXT, Price DECIMAL(10, 2), Stock INTEGER)");
  foreach (var fish in [{ name: "Neon tetra", price: 2.5m, stock: 120 }, { name: "Discus", price: 49m, stock: 6 }]) {
    await db.execute("INSERT INTO Fish (Name, Price, Stock) VALUES (@name, @price, @stock)", fish);
  }
  writeLn(await db.query("SELECT Name, Price FROM Fish WHERE Price < @max", { max: 10 }));
  var left = await db.transaction(async () => {
    await db.execute("UPDATE Fish SET Stock = Stock - @sold WHERE Name = @name", { sold: 2, name: "Discus" });
    return (await db.query("SELECT Stock FROM Fish WHERE Name = 'Discus'"))[0]["Stock"];
  });
  writeLn([left, db.provider]);
  try { await db.query("SELECT * FROM Tanks"); } catch (DatabaseError e) { writeLn([e.code, e.message]); }
}
```

```text
[{"Name": "Neon tetra", "Price": 2.5m}]
[4, "sqlite"]
["WL3015", "SQLite Error 1: 'no such table: Tanks'."]
```

---

## 18. `std:crypto`

Cryptography, through the platform's: random bytes, hashes, HMAC, a comparison in constant time, and passwords'
hashes. Data, a key and a password are a string, as UTF-8, or `Bytes`; a hash is `Bytes`.

| Function | What it gives |
|---|---|
| `randomBytes(n)` | `n` secure random bytes, 0 to 1,048,576 |
| `sha256(data)`, `sha512(data)` | the data's SHA-256 or SHA-512 |
| `hmac(algorithm, key, data)` | the data's HMAC with the key, `"sha256"` or `"sha512"` |
| `equal(a, b)` | whether two values are the same bytes, compared in a time that does not tell where they differ |
| `hashPassword(password)` | a password's hash, PBKDF2 with HMAC-SHA-256, 600,000 iterations and a random salt, `"pbkdf2-sha256$600000$salt$hash"` |
| `verifyPassword(password, hash)` | whether the password is the one the hash was made of |
| `hashPasswordAsync(password)`, `verifyPasswordAsync(password, hash)` | the same, as a task, run off the worker |

`hashPassword` takes a large part of a second by design, as a password's hash should, and blocks its worker meanwhile:
a site's page awaits `hashPasswordAsync` instead, so that its worker serves other requests. A hash names its iterations
and its salt, so that one made with today's defaults still verifies after they change.

```javascript
import { writeLn } from "std:console";
import crypto from "std:crypto";
writeLn(crypto.sha256("abc").toHex());
writeLn(crypto.hmac("sha256", "Jefe", "what do ya want for nothing?").toHex());
writeLn(crypto.randomBytes(16).length);
var stored = await crypto.hashPasswordAsync("correct horse");
writeLn([stored.startsWith("pbkdf2-sha256$600000$"), await crypto.verifyPasswordAsync("correct horse", stored),
  crypto.verifyPassword("wrong", stored)]);
```

```text
ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843
16
[true, true, false]
```

A hash, an HMAC and random bytes are `Bytes`, which `toHex()` and `toBase64()` write as text (section 10). An algorithm it does not have, a value that is not data, and a hash it did not make are each WL3001.

---

## 19. `std:env`

The machine's environment, through the platform's: its name and its environment variables, as a class, `Environment`,
whose members are static.

| Member | What it is |
|---|---|
| `Environment.machineName` | the machine's name |
| `Environment.architecture` | the machine's processor, `"x64"`, `"x86"`, `"arm64"` or `"arm"` |
| `Environment.processArchitecture` | the running program's, as `architecture` names it: `"x86"` for a 32-bit program on 64-bit Windows |
| `Environment.os` | the operating system, `"windows"`, `"linux"` or `"macos"`, `"freebsd"` too, `"other"` for any other |
| `Environment.variable(name)` | a variable's value, a string, or `null` when it is not set |
| `Environment.variables` | a map of the variables, each its name and its value |
| `Environment.setVariable(name, value)` | sets a variable, for the process alone, or removes it, given `null` |

A script under `weblingo` reads all of it, and sets variables. A site's pages and handlers, under `wasp`, read only what
the site's `wasp.config` allows, `{ "env": { "machineName": true, "platform": true, "variables": ["PATH", "APP_*"] } }`:
the machine's name with `"machineName": true`, its architectures and its system with `"platform": true`, and the variables it lists, a name ending in `*` allowing those it begins, `"*"` any. Anything
else is `null`, as if it were not there, and missing from `variables`. A site sets no variable: `setVariable` is an
`EnvironmentPermissionError`, WL3019. A variable's name is compared as the platform compares it, ignoring case on
Windows; the map's names are as the platform gives them.

```javascript
import { writeLn } from "std:console";
import { Environment } from "std:env";
Environment.setVariable("GREETING", "hello");
writeLn(Environment.variable("GREETING"));
writeLn(Environment.variables["GREETING"]);
Environment.setVariable("GREETING", null);
writeLn(Environment.variable("GREETING"));
writeLn(Environment.machineName.length > 0);
```

```text
hello
hello
null
true
```

A name that is not a string, a name to set that is empty or holds `=`, and a value that is neither a string nor `null`,
are each WL3001.

---

## 20. `std:math`

Mathematics, imported as a module, `import math from "std:math";`: its functions and its constants are its members.

| Member | What it gives |
|---|---|
| `math.abs(x)`, `math.sign(x)` | `x`'s absolute value; -1, 0 or 1 as `x` is negative, zero or positive, `NaN` for `NaN`; of `x`'s kind, an integer, a number or a decimal |
| `math.min(a, …)`, `math.max(a, …)` | the least, or the greatest, of one value or more, as it was given, its kind kept, compared as `<` compares them; `NaN` where one is `NaN` |
| `math.floor(x)`, `math.ceiling(x)`, `math.truncate(x)` | an integer: the greatest not above `x`, the least not below it, `x` without its fraction |
| `math.round(x)` | the nearest integer, halfway away from zero, `2.5` to `3` and `-2.5` to `-3` |
| `math.sqrt(x)`, `math.pow(x, y)`, `math.exp(x)` | a number: the square root, `x` to the power `y`, e to the power `x` |
| `math.log(x)`, `math.log10(x)`, `math.log2(x)` | a number: the natural logarithm, and those of bases 10 and 2 |
| `math.sin(x)`, `math.cos(x)`, `math.tan(x)`, `math.asin(x)`, `math.acos(x)`, `math.atan(x)`, `math.atan2(y, x)` | a number: the trigonometric functions, of radians, and their inverses, `atan2` the angle of the point `(x, y)` |
| `math.PI`, `math.E` | the numbers π and e |

`floor`, `ceiling`, `round` and `truncate` take an integer, which they give as it is, a number or a decimal, and give an
integer; one beyond 64 bits, infinity among them, is an `OverflowError`, WL3002, and `NaN` WL3001. The functions that
give a number take an integer or a number, as IEEE 754 has them, `math.sqrt(-1.0)` being `NaN` and `math.log(0)`
`-Infinity`; a decimal is WL3001, as nothing converts by itself. A number and a decimal are not compared, `math.max(1.5,
2m)` WL3001, as `1.5 < 2m` is.

```javascript
import { writeLn } from "std:console";
import math from "std:math";
writeLn([math.floor(2.7), math.round(2.5), math.round(-2.5), math.truncate(-2.7), math.floor(2.7) is Integer]);
writeLn([math.abs(-3), math.abs(-1.50m), math.min(3, 1, 2), math.max(...[4.5, 9, 2])]);
writeLn([math.sqrt(2), math.pow(2, 10), math.PI]);
```

```text
[2, 3, -3, -2, true]
[3, 1.5m, 1, 9]
[1.4142135623730951, 1024.0, 3.141592653589793]
```

---

## 21. Native modules

A native module is code compiled for .NET that extends what scripts can use: functions and classes, imported as any
module's are, by a scheme naming a folder of them, `import db from "native:sqlite";`. The folders are named by the
configuration, `weblingo.config` beside a script and `wasp.config` in a site, `{ "schemes": { "native":
"C:/WebLingo/native" } }`, and by the command line's `--scheme native=folder`. The standard modules, `std:system`,
`std:console`, `std:tasks`, `std:fs`, `std:http`, `std:net`, `std:db`, `std:crypto`, `std:env`, `std:math`, `std:mail`, `std:workers` and `std:shared`, are native modules themselves, in the `std`
folder beside `weblingo` and `wasp`. A native module's function may have optional parameters, or a rest parameter, as
`math.max(a, …)` has, and a module may export constants, as `math.PI` is, and values made for each program where it
begins, as `std:workers`' `parent` is, which no importer may assign.

---

## 22. In WASP's pages

A page, a layout and a component that declare `param request;` and `param response;` are given the request they
answer and the response they make, instances of the built-in classes `Request` and `Response`, their headers instances
of `Headers`, and the request's session of `Session`; a `.wlg` route handler is given them as its function's arguments.
How a site uses them is the WASP User Manual's, section 9.

The classes' names are not in scope, `request is Request` being WL2001: a script reaches them as
`request.__instanceOf` and the like. Only the host makes their instances, which are written `Request {}`, `Response {}`,
`Headers {}` and `Session {}`; one made by `new` holds no request, each of its members WL3001.

### `Request`

| Member | What it is |
|---|---|
| `request.method` | its method, `"GET"`, `"POST"`, as the client sent it |
| `request.path` | its path, from `/`, decoded, without the query: `/greet` |
| `request.scheme`, `request.host` | `"http"` or `"https"`, and the host the client asked for, with its port if it gave one |
| `request.clientAddress` | the client's address, `"203.0.113.7"` or `"2001:db8::7"`, without its port: its connection's, the web server's word behind FastCGI, and a trusted proxy's behind one (`wasp.config`'s `"proxies"`); `""` where its host does not know it |
| `request.query` | the query's names and values, decoded, a map, a name given twice having its last value |
| `request.headers` | its headers, a `Headers`, which cannot be changed |
| `request.cookies` | its cookies, a map of their names to their values, decoded, a name sent twice having its first value |
| `await request.text()` | its body as text, in the charset its `Content-Type` names, UTF-8 if it names none |
| `await request.form()` | a form's fields, a map: an `application/x-www-form-urlencoded` one's, decoded as the query is, or a `multipart/form-data` one's; a body of another type WL3001 |
| `await request.files()` | a `multipart/form-data` form's files, a map of each file input's name to an array of `UploadedFile`s; an `application/x-www-form-urlencoded` form's none, an empty map; a body of another type WL3001 |
| `await request.read()` | the body's next piece as it arrives, `Bytes`, `null` at its end |
| `request.session` | its session, a `Session`, the same each time; a `SessionLockError`, WL3024, where its lock was waited for longer than the site waits, and WL3001 where the host keeps no sessions |
| `request.csrfToken` | its token against cross-site request forgery, 32 hexadecimal digits, the same each time: its `WASPCSRF` cookie's, or a new one, that cookie set, `HttpOnly` and `SameSite=Lax`; WL3001 once its response has been sent |
| `await request.checkCsrf(token)` | whether `token` is its `WASPCSRF` cookie's, compared in constant time, `false` without the cookie; with no token, its `X-CSRF-Token` header's, else its form's `csrfToken` field's; a token neither a string nor `null` WL3001 |

An `UploadedFile`, a file a form uploaded: `name`, its input's; `fileName`, as the browser gave it, never a path;
`type`, its `Content-Type`; `size`, in bytes; and `await file.bytes()`, `await file.text()`, in UTF-8, and
`await file.save(path)`, which writes it at `path`, a file there replaced, within the folders the site may write, a
`FilePermissionError`, WL3016, elsewhere. A `multipart/form-data` form is read once, as it arrives, `form()` and
`files()` reading the same body, which `text()` and `read()` then cannot, WL3001. A body larger than the site takes,
its `wasp.config`'s `"maxBodySize"`, 30,000,000 bytes unless it says otherwise, is WL3001 where it is read, and its
files larger than its `"uploads"`' `"maxSize"` too.

A body is read only when a script asks for it. Read whole, it is kept, `text()` and `form()` giving the same again, and
`read()` giving it as one piece; read in pieces, it cannot then be read whole, WL3001, and neither can `read()` be called
again before the piece it gives has come.

### `Response`

| Member | What it is |
|---|---|
| `response.status` | its status, an integer, 200 until set; one outside 100 to 999 WL3001 |
| `response.headers` | its headers, a `Headers`, which can be changed until the response is sent; the site's own when the page begins, `X-Content-Type-Options` and its kin (the WASP User Manual, section 9, "Security headers") |
| `response.write(value)` | writes a value's text to its body, as `write` writes it, `null` as `null`, giving `null`; `Bytes` as they are |
| `response.writeLn(value)` | the same, then a line break |
| `response.sendFile(path, type, options)` | a task that makes the file at `path` the response, giving `null`; `type` and `options` may be left out |
| `response.setCookie(name, value, options)` | sets a cookie, its value a string, giving `null`; `options`, a map, or `null` |
| `response.deleteCookie(name, options)` | deletes a cookie, its `options` naming its `path` and `domain`, giving `null` |

A cookie is `HttpOnly`, `SameSite=Lax`, `Path=/`, and `Secure` over HTTPS, unless its options say otherwise:
`httpOnly` and `secure`, Booleans; `sameSite`, `"lax"`, `"strict"` or `"none"`, the last for a secure cookie only;
`path`, from `/`; `domain`, a host's name; `maxAge`, seconds, an integer, 0 or more; and `expires`, a `DateTime`. Its
value is sent with each byte a cookie cannot hold, and `%`, as `%xx`, and `request.cookies` decodes it. A name that is
not a cookie's, a value that is not a string, an option that is not one, its error naming the one it may have meant,
or a wrong option's value, is WL3001; and so is a cookie set or deleted once the response is sent. A cookie set again,
of the same name, path and domain, replaces the one set before in the same response.

A route handler writes its response's body with `response.write` and `response.writeLn`, as classic ASP's
`Response.Write`; a page writes it with its text, its `<%= %>` and its own `write` and `writeLn` too, which write as
`response.write` and `response.writeLn` do, and need no `param response;`. A call of either as a statement waits for a
slow client, as a page's writes do. A response is sent once its output reaches 64 KB (the WASP User Manual, section
13): setting its status or a header after that is WL3001.

`await response.sendFile(path, type, options)` sends a file as the response (the WASP User Manual, section 9, "Sending a
file"): its path a script's, within the folders `wasp.config`'s `"files"` lets the site read, one it may not a
`FilePermissionError`, WL3016, and a file that is not there, or a folder, an `IOError`, WL3011; `type` a media type,
`"application/pdf"`, or `null` for the response's `Content-Type`, else the one its extension gives, else
`application/octet-stream`; `options` a map, or `null`, whose `download` is the name a browser saves it under, `true`
for its own, `false` or `null` for it shown. Any other value, or option, is WL3001, and so is a file sent once the
response is being sent. The response keeps its status and headers; what was written before it, and after, is not sent.
The file comes with its `ETag`, its `Last-Modified` and `Accept-Ranges: bytes`, a client that has it answered 304, and
one range of it 206.

### `Session`

A browser's session, which the site keeps between its requests, in the server's memory or in a table of its database
(the WASP User Manual, section 9, "Sessions" and "Sessions in a database"):

| Member | What it is |
|---|---|
| `s[name]` | the value of `name`, a copy made anew for each read; a missing one WL3007, `s?[name]` giving `null` |
| `s[name] = value` | keeps a copy of `value`, the session made, and its cookie set, where there is none |
| `s.has(name)` | whether it keeps a value of that name |
| `s.remove(name)` | removes it, giving whether it was there |
| `s.release()` | gives its lock up, its values written, giving `null`; a change after it WL3001 |
| `s.renew()` | gives it a new id, its values and its lock kept, its cookie sent anew, giving `null`; nothing where there is no session |
| `s.end()` | removes it, its cookie deleted where the response has not been sent, giving `null`; a value set after it makes a new one |

A session keeps `null`, Booleans, integers, finite numbers, strings, and arrays and maps of them, a map's keys
strings, written as JSON, each read back as the kind it was, `5.0` a number: anything else, a value that holds itself,
and one nested more than 512 deep, is WL3001 where it is set, the message naming what it cannot keep. A name that is not
a string is WL3001. Making a session, or renewing it, once the response is sent is WL3001, its cookie being a header;
and so is any change in a request that declares that it only reads its session, `session readonly;` in a page,
`export const session = "readonly";` in a handler, its message naming the declaration, or after `release()`. Where
sessions are kept in a database, one that fails, or whose table of sessions is missing, is a `DatabaseError`, WL3015,
where a page reads its session, makes, renews or ends it, its message naming the command that makes the table.

### `Headers`

| Member | What it is |
|---|---|
| `h[name]` | a header's value, its values joined by `", "`, its name matched without regard to case; a missing one WL3007, `h?[name]` giving `null` |
| `h[name] = value` | sets it, in place of the values it had |
| `h.add(name, value)` | gives it one more value, as `Set-Cookie` needs |
| `h.has(name)` | whether it is there |
| `h.remove(name)` | removes it, giving whether it was there |

A name or a value that is not a string, a name HTTP does not allow, a value with a character a header's cannot have,
and any change to a request's headers, are WL3001.

### Components and layouts

A page also has `renderComponent(path, values)`, a task, which renders a component, and a layout `renderBody()`, which
renders its page's body where it stands (the WASP User Manual, sections 6 and 7).

### `Html` and `std:html`

A page's `<%= value %>` is escaped for where it stands in the page's HTML (the WASP User Manual, section 4). For where
WASP cannot tell, the class `Html`, which a program has by importing it, `import { Html } from "std:html";`, in a page
too, gives a value's text, as `write` writes it but for an instance's `toString()`, escaped; `null` gives `""`:

| Member | What it gives |
|---|---|
| `Html.escape(x)` | `&`, `<`, `>`, `"` and `'` as entities |
| `Html.url(x)` | a URL: `#zWebLingoz` if its scheme is not `http`, `https` or `mailto`, else percent-encoded as a URL needs, as UTF-8 |
| `Html.urlPart(x)` | percent-encoded whole, but letters, digits and `-._~`, for a URL's query or fragment |
| `Html.js(x)` | `x`'s JSON, as `Json.stringify` writes it, with `<`, `>`, `&` and line separators as escapes, a number or a keyword between spaces; WL3001 for what JSON cannot write |
| `Html.jsString(x)` | JavaScript's escapes, for inside a script's string |
| `Html.css(x)` | `zWebLingoz` if it could change what a style means, else the value, its CSS escapes decoded |

```javascript
import { writeLn } from "std:console";
import { Html } from "std:html";
writeLn(Html.escape("<b>Tea & cake</b>"));
writeLn(Html.url("javascript:alert(1)"));
writeLn(Html.url("/teas/green tea"));
writeLn(Html.urlPart("green tea & cake"));
writeLn(Html.js({ name: "Sencha", cups: 2 }));
writeLn(Html.jsString("It's </script>"));
writeLn(Html.css("red; background: url(x)"));
```

```text
&lt;b&gt;Tea &amp; cake&lt;/b&gt;
#zWebLingoz
/teas/green%20tea
green%20tea%20%26%20cake
{"name":"Sencha","cups":2}
It\u0027s \u003c\/script\u003e
zWebLingoz
```

`new Html()` is WL3001: the class has no instances.

---

## 23. `windows:com`

COM's objects, on Windows only, as classic ASP's pages used them, `Server.CreateObject("ADODB.Connection")`: a native
module installed with the runtime in `windows/`, beside `std/`. Elsewhere, importing it is WL2015.

| Member | What it is |
|---|---|
| `com.create(progId)` | a new object, a `ComObject`, by its ProgID, `"ADODB.Connection"`, or its CLSID, `"{00000514-…}"` |
| `com.get(moniker)` | an object that exists, bound to by its moniker, as `GetObject` binds |
| `x.Name`, `x.Name = v`, `x.Method(a, b)` | the object's members, through its `IDispatch`, their names' case aside; a property that takes arguments is read as a call, `rs.Fields.Item("Name")` |
| `x[k]`, `x[k] = v` | its default member with `k`: `rs["Name"]` is the field, as `rs("Name")` was |
| `foreach (var item in x)` | a collection's items, through its `_NewEnum` |
| `x.iterate()` | the iterator `foreach` visits them by, a `ComEnumerator` |
| `x.call(name, arguments)`, `x.get(name)`, `x.set(name, value)` | a member that one of `ComObject`'s own hides, `toString`, `dispose` or `get` |
| `x.dispose()` | releases the object, as `using` does; the collector or the program's end releases one never disposed |

A script makes only the objects its configuration allows, `weblingo.config` beside it, or a site's `wasp.config`, by
ProgID, CLSID or moniker, `"*"` allowing any:

```json
{ "com": { "allow": ["ADODB.Connection", "ADODB.Recordset"] } }
```

Any other is a `ComPermissionError`, WL3018. Values go to COM and come back as their kinds: `null` as `VT_NULL` and back; Booleans; integers,
as `VT_I4` or `VT_I8`; numbers; decimals, ADO's `MONEY` and `DECIMAL` among them; strings; `Bytes`, ADO's binary fields;
dates, at their time on the server's clocks; arrays, a `SAFEARRAY` of several dimensions coming back as arrays of
arrays; and objects, as `ComObject`s. A member the object does not have is a `MemberError`, WL3006, and a failure a
`ComError`, WL3012, whose `number` is its `HRESULT`, and whose `source` and `description` are what COM said, `""`
where it said nothing:

```javascript
import { writeLn } from "std:console";
import com from "windows:com";
var tea = com.create("Scripting.Dictionary");
tea.Add("Sencha", 6.5m);
tea["Assam"] = 4.25m;
writeLn([tea.Count, tea.Item("Sencha"), tea["Assam"], tea.Exists("Oolong")]);
foreach (var name in tea) writeLn(name);
try { tea.Add("Sencha", 7); } catch (ComError e) { writeLn(e.number); }
```

```text
[2, 6.5m, 4.25m, false]
Sencha
Assam
-2146827831
```

A `ComEnumerator` is an iterator over a collection's `_NewEnum`, which reads one item ahead (section 5):

| Member | What it is |
|---|---|
| `e.hasNext()` | whether the collection has another item |
| `e.next()` | its next item; WL3005 past the last |
| `e.dispose()` | releases the collection's enumerator, as `using` does; the collector releases one never disposed |

```javascript
import { writeLn } from "std:console";
import com from "windows:com";
var tea = com.create("Scripting.Dictionary");
tea.Add("Sencha", 6.5m);
tea.Add("Assam", 4.25m);
using (var names = tea.iterate()) {
    writeLn([names, names is com.ComEnumerator, tea is com.ComObject]);
    while (names.hasNext()) writeLn(names.next());
}
```

```text
[ComEnumerator {}, true, true]
Sencha
Assam
```

`ComObject` and `ComEnumerator` are the module's exports, `com.ComObject`, or `import { ComObject } from "windows:com";`,
for `is`. Only the module makes their instances: one made by `new` holds no object, each of its members WL3001.

A call blocks its worker until the object returns, each worker a single-threaded apartment's thread, its objects its
own. Events, arguments by reference and named arguments are not there yet.

## 24. `std:path`

These helpers work on path strings without accessing the filesystem. They accept `/` and `\` on both platforms and
emit the host separator. They recognize drive prefixes and UNC shares on both platforms.

- `join(...parts)` joins the nonempty parts. A rooted or drive-prefixed part starts a new path. It leaves `.` and `..` as written.
- `dirname(path)` gives the parent path, or the empty string for a lone relative filename. A root is its own parent.
- `basename(path)` gives the final component, ignoring trailing separators.
- `extension(path)` gives the final extension with its dot, or the empty string for names such as `.profile`.

```javascript
import { writeLn } from "std:console";
import path from "std:path";
var file = path.join("reports\\daily", "sales.csv");
writeLn([path.basename(file), path.extension(file)]);
```

```text
["sales.csv", ".csv"]
```

## 25. `std:url`

- `parse(url)` takes an absolute URL and returns a map with `scheme`, `host`, `port`, `path`, `query`, and `fragment`.
  The port is `null` when the scheme's default applies. Query and fragment omit their leading delimiter.
- `resolve(base, relative)` resolves a URL reference against an absolute base and returns an absolute URL string.
- `parseQuery(query)` decodes ordered `[name, value]` string pairs. It accepts a leading `?`, decodes `+` as a space,
  and gives a missing value as the empty string.
- `formatQuery(pairs)` encodes the pairs in order, with spaces as `+`, and returns the query without a leading `?`.
  Repeated names remain separate pairs.

```javascript
import { writeLn } from "std:console";
import url from "std:url";
writeLn(url.resolve("https://example.test/menu/", "../tea"));
writeLn(url.parseQuery("tea=green&tea=black"));
```

```text
https://example.test/tea
[["tea", "green"], ["tea", "black"]]
```

## 26. `std:csv`

`parse(text, headers = false)` returns rows as arrays of strings. With `headers = true`, it reads the first record
as the header and returns the remaining rows as maps. Headers must be unique, and each following row must have
the same number of fields. The parser accepts CRLF, LF, and CR record endings and preserves newlines inside quoted fields.
It consumes a byte-order mark at the start of the text.

`stringify(rows, headers = null)` writes arrays of strings with CRLF record endings. To write maps, supply a nonempty
array of unique header strings in the desired order. Each map must supply a string for every header.
Fields containing commas, quotation marks, or newlines are quoted, with quotation marks doubled.
A first field or header beginning with a literal U+FEFF is also quoted so the character survives parsing.

```javascript
import { writeLn } from "std:console";
import csv from "std:csv";
var text = csv.stringify([{ tea: "sencha", note: "warm, please" }], ["tea", "note"]);
var rows = csv.parse(text, true);
writeLn([rows[0]["tea"], rows[0]["note"]]);
```

```text
["sencha", "warm, please"]
```

## 27. `std:compression`

| Function | Result |
|---|---|
| `gzip(data)` | gzip-compressed `Bytes` |
| `gunzip(data, maxBytes)` | decompressed `Bytes` |
| `zip(entries)` | ZIP archive as `Bytes`. Pass a map of file names to `Bytes` |
| `unzip(data, maxBytes)` | map of file names to decompressed `Bytes` |

`maxBytes` is required and must be a whole number from 0 to 2,147,483,647. For ZIP it caps the sum of the files'
decompressed byte counts. Exceeding the limit raises `TypeError` (WL3001). The execution's heap budget also applies.
ZIP entry paths are map keys. The module skips directory records, rejects duplicate file names and uses no filesystem access.

```javascript
import { writeLn } from "std:console";
import compression from "std:compression";
import { Bytes } from "std:bytes";

var data = Bytes.from("Sencha");
var compressed = compression.gzip(data);
writeLn(compression.gunzip(compressed, 100).text());

var archive = compression.zip({ "menu/tea.txt": data });
writeLn(compression.unzip(archive, 100)["menu/tea.txt"].text());
```

```text
Sencha
Sencha
```

## 28. `std:random`

`new Random(seed)` creates a generator for reproducible tests and simulations. The seed is a signed 64-bit
integer. Each instance has its own state. Equal seeds give identical sequences for identical calls.

- `rng.next()` returns a number from 0 inclusive to 1 exclusive.
- `rng.nextInt(min, max)` returns an integer in the range from `min` inclusive to `max` exclusive, without
  bias from fitting the random bits to the range. Both bounds must be integers and `min` must be less than `max`.

Use `std:crypto` for secure randomness.

```javascript
import { writeLn } from "std:console";
import { Random } from "std:random";

var rng = new Random(42);
writeLn([rng.nextInt(0, 100), rng.nextInt(0, 100), rng.nextInt(0, 100)]);
```

```text
[13, 91, 58]
```

## 29. `std:process`

`await process.run(program, args = null, options = null)` starts a local executable from a `weblingo` script.
Pass an array of argument strings. The runtime launches the executable directly and closes its standard input.
The child uses the CLI's working directory and environment.

The result is a map with an integer `exitCode` and the captured `stdout` and `stderr` strings, decoded as UTF-8.
A nonzero exit code is returned in the map. `options` may contain either of these keys.

| Option | Default | Allowed values |
|---|---|---|
| `timeoutMs` | 30,000 | integers from 1 to 2,147,483,647 |
| `maxOutputBytes` | 1,048,576 | integers from 0 to 16,777,216, counting both streams together |

A timeout raises `TimeoutError` (WL3010). A launch failure or an exceeded output limit raises `IOError` (WL3011).
On Windows and Linux, a timeout or exceeded output limit stops the launched program and its descendants in the
same job or process group, including when the original program has already exited. On Windows, the error is raised
only once they have ended, so that a script may remove the files they had open at once.
WASP returns `IOError` for subprocess calls.

```javascript
param program = "weblingo";
import { writeLn } from "std:console";
import process from "std:process";

var child = await process.run(program, ["--version"]);
writeLn([child["exitCode"], child["stdout"].startsWith("WebLingo ")]);
```

```text
[0, true]
```

## 30. `std:test`

Import assertions by name. Save this example as `arithmetic.wlg` and run `weblingo test arithmetic.wlg`.

```javascript
import { assertEqual, assertClose, checkAll } from "std:test";

export class ArithmeticTests {
    beforeEach() {
        this.values = [2, 3, 4];
    }

    testSum() {
        assertEqual(this.values[0] + this.values[1], 5, "The first two values sum to five.");
    }

    async testIndependentResults() {
        await checkAll(check => {
            check.assertEqual(this.values.length, 3);
            check.assertClose(0.1 + 0.2, 0.3, {absolute: 0.000001});
        });
    }
}
```

`weblingo test` takes one or more `.wlg` files in the order given on the command line. It discovers classes
declared at the top level with `export class` and a name ending in `Tests`. A case is a directly declared
instance method with a name starting with `test` followed by an uppercase letter. Cases run in declaration
order and must declare zero parameters. The runner initializes the file once, creates a fresh instance per case,
and awaits each case. Optional zero-argument `beforeEach` and `afterEach` instance methods may be synchronous
or asynchronous. Cleanup runs even when setup or the case throws, provided construction succeeded. If cleanup
also fails, both failures are reported. The file's `weblingo.config` selects the VM.

- `weblingo test --list arithmetic.wlg` prints discovered case labels without running the module.
- `weblingo test --filter testSum arithmetic.wlg` selects cases with a case-sensitive substring of the
  `file::Class.method` label. The two options can be combined.

Failures include the case label and assertion location. The runner continues with the remaining cases and
exits nonzero if a case fails, discovery fails, or the filter selects no cases.

| Function | Checks |
| --- | --- |
| `assert(condition, message = null)` | A Boolean condition is true. |
| `assertEqual(actual, expected, message = null)` | The values are equal by WebLingo's `==` operator. |
| `assertNotEqual(actual, expected, message = null)` | The values are unequal by the same operator. |
| `assertDeepEqual(actual, expected, message = null)` | Array elements in order, maps by keys and values regardless of insertion order, sets by membership, and bytes by content. Handles cycles and reports the first differing path. |
| `assertContains(actual, item, message = null)` | An array or set contains the item, or a string contains a case-sensitive substring. |
| `assertHasKey(map, key, message = null)` | A map contains the key, including when its value is `null`. |
| `assertMatches(text, regex, message = null)` | A `Regex` from `std:regex` matches the text. |
| `assertClose(actual, expected, tolerance, message = null)` | The numeric difference is within an absolute or relative tolerance. |
| `assertThrows(action, expectedClass = Error)` | The zero-argument action throws the expected error class or a subclass synchronously. Returns the error. |
| `await assertRejects(action, expectedClass = Error)` | The zero-argument action returns a task that rejects with the expected error class or a subclass. Returns the error. |
| `await checkAll(action)` | Passes the callback a checker with the same assertion methods and reports the failures together. |

For `assertClose`, pass a map with `absolute`, `relative`, or both. The tolerances must be finite, nonnegative
numbers. Relative tolerance is multiplied by the larger magnitude of the actual and expected values. If you
provide both tolerances, the larger allowed difference applies.

Ordinary assertions throw `Error` with code `WL3021` when they fail. A comparison failure shows the expected
value, the actual value, and any supplied message. In a `checkAll` callback, call the assertions on the supplied
checker to record failures and continue. An unexpected exception stops the callback and is reported with the
failures collected so far. Await `checkAll` to observe the result.

---

## 31. `std:mail`

Mail, sent by SMTP to a relay, a server that sends it on, as a provider's or an organization's does:
`import mail from "std:mail";`, then `send`, asynchronous, a task of the message's `Message-ID`, sent off the worker,
so that a page's other tasks go on while it waits.

| Function | What it does |
|---|---|
| `send(message, server)` | sends `message`, a map, through `server`, which may be left out; gives its `Message-ID`, without its angle brackets |

A message is a map of these, `from` and at least one recipient required, any other left out or given `null`:

| Key | What it holds |
|---|---|
| `from` | the sender's address: `"ann@example.com"`, `"Ann <ann@example.com>"` or `"\"Smith, Ann\" <ann@example.com>"` |
| `to`, `cc`, `bcc` | the recipients, each an address or an array of them; a `bcc` is sent to, never written in the message |
| `replyTo` | where a reply goes, an address or an array of them |
| `subject` | a string, on one line |
| `text`, `html` | the message, as text, as HTML, or both, which a reader shows as it can |
| `headers` | other headers, a map of names to strings, `{ "X-Order": "1042" }`, none of the message's own, `Subject`, `Content-Type` and the like |
| `attachments` | an array of files, each `{ path: "…" }`, a file's, or `{ name: "receipt.txt", content: … }`, its content a string or bytes, and `type`, its `Content-Type`, which may be left out |

Every key is checked, and every value's kind: a key a message does not have is WL3001, naming the one it may have
meant, `'subjet' is not a field of a message; did you mean subject?`, and so is an address that is not one, or that is
not ASCII, which mail cannot use yet. A subject and a header's value may hold any letters, and the text and the HTML
any; an attachment's type, where it is left out, is its name's extension's, `application/pdf` for `.pdf`.

The server is configured, in `weblingo.config` beside a script, or a site's `wasp.config`, each by a name, a send that
names none using `"default"`, or the one there is:

```json
{
  "mail": {
    "default": { "host": "smtp.example.com", "port": 587, "user": "shop@example.com", "password": { "env": "SMTP_PASSWORD" } },
    "newsletter": { "host": "mail.example.net", "port": 465, "tls": "implicit", "user": "news", "password": { "file": "../secrets/news.txt" } }
  }
}
```

| Setting | What it says | Default |
|---|---|---|
| `"host"` | the server's name or address | required |
| `"port"` | its port, from 1 to 65535 | 587, 465 for `"implicit"`, 25 for `"none"` |
| `"tls"` | `"starttls"`, TLS begun once connected, `"implicit"`, TLS from the start, or `"none"`, no TLS | `"starttls"` |
| `"user"`, `"password"` | who it signs in as, and the password, which needs a user | none: no sign-in |
| `"thumbprint"` | the SHA-256 or SHA-1 thumbprint of a certificate accepted though it cannot be checked, in hexadecimal | none |

A password, as a database's connection string, may be given where it is kept rather than as itself, so that the
configuration need not hold it: `{ "env": "SMTP_PASSWORD" }`, an environment variable, or `{ "file":
"../secrets/smtp.txt" }`, a file, from the configuration's folder, which a site keeps outside its own, since its files
may be served; either is read each time it is used, and a script never sees it, nor does any error show it.

`server`, where given, names a configured server, `"newsletter"`, or is a map: `{ token: "…" }`, or `{ server:
"newsletter", token: "…" }`, a configured server signed in to by XOAUTH2, with an OAuth 2.0 token the script got, as
Google's and Microsoft's servers take; or a server of the script's own, `{ host: "…", port: 587, user: "…", password:
"…", token: "…", tls: "starttls", thumbprint: "…" }`, every key but `host` optional, which a site may reach only where
its `wasp.config`'s `"network"` allows its host, a `NetworkPermissionError`, WL3017, otherwise.

Mail goes encrypted: a server that does not offer STARTTLS is refused, unless its settings say `"tls": "none"`; its
certificate is checked, its chain and its name, one that cannot be, a self-signed one among them, accepted only by its
`"thumbprint"`, which the error names; TLS is 1.2 or later; and a password is never sent without TLS but to a server on
this machine. A server signs in by XOAUTH2 where a token is given, else by PLAIN, else by LOGIN.

A message that cannot be sent is a `MailError`, WL3023: a server that cannot be reached, offers no TLS, refuses the
sign-in, a recipient or the message; its `reply` is the server's reply code, `550`, or `null` where it gave none.
Nothing is sent where a recipient is refused. A name the configuration does not give is WL3001, and a file an attachment
names that cannot be read an `IOError`, WL3011.

Given a mail server on this machine, at port 2525, without TLS, that refuses `nobody@example.com`, and a
`weblingo.config` beside the script, `{ "mail": { "default": { "host": "localhost", "port": 2525, "tls": "none" } } }`:

```javascript
import { writeLn } from "std:console";
import mail from "std:mail";
var id = await mail.send({
    from: "Tea Shop <shop@example.com>",
    to: ["ann@example.com", "Bob <bob@example.com>"],
    subject: "Your order",
    text: "Your Sencha is on its way.",
    html: "<p>Your <b>Sencha</b> is on its way.</p>",
    attachments: [{ name: "receipt.txt", content: "Sencha 4.50" }]
});
writeLn(id.endsWith("@example.com"));
try {
    await mail.send({ from: "shop@example.com", to: "ann@example.com", subjet: "Hi" });
} catch (e) {
    writeLn(e.message);
}
try {
    await mail.send({ from: "shop@example.com", to: "nobody@example.com", text: "Hi" });
} catch (MailError e) {
    writeLn([e.code, e.reply]);
}
```

```text
true
'subjet' is not a field of a message; did you mean subject?
["WL3023", 550]
```

What is not there yet: addresses that are not ASCII (SMTPUTF8), signing a message (DKIM), reading mail (IMAP, POP3),
and a class of messages, `MailMessage`.

## 32. `std:log`

A log, `import log from "std:log";`: each function writes a line of its level, the levels from the lowest `debug`,
`info`, `warn` and `error`, giving `null`.

| Function | What it does |
|---|---|
| `debug(text)`, `info(text)`, `warn(text)`, `error(text)` | writes `text`, a string, as a line of that level; anything else WL3001, `$"{value}"` making a string of a value |

A line is its time, in UTC, to the millisecond, its level and its text, kept to one line, a line break in it written
`\n`, a carriage return `\r` and any other control character but a tab `\u{…}`. Under `weblingo` it goes to the
standard error, every level; under `wasp` to the site's log, its request named after its level, `GET /cart #12`, from
the level its `wasp.config`'s `"log"` gives on, `info` unless it says otherwise (the WASP User Manual, section 12, "The
log").

```javascript
import log from "std:log";
import { writeLn } from "std:console";
log.info("Starting");
writeLn("working");
log.warn($"{3} items left");
log.debug("each line its time in UTC");
```

```text
2026-11-25T08:24:05.902Z INFO Starting
working
2026-11-25T08:24:05.905Z WARN 3 items left
2026-11-25T08:24:05.905Z DEBUG each line its time in UTC
```

The output and the log are written to the terminal as they come; the log alone, `weblingo run script.wlg 2> log.txt`.

---

## 33. `std:workers`

Work done in parallel, on the machine's other processors: `import { Worker, parent } from "std:workers";`, under
`weblingo` alone for now, its import WL2015 under `wasp`. `new Worker(path)` runs a module, a `.wlg` file of the
program's, on a worker of its own, a thread with a heap of its own, which shares nothing with its parent: the two
exchange values, each copied whole.

| Member | What it does |
|---|---|
| `new Worker(path)` | runs the module at `path`, from the program's own folder, `.wlg` added to a path without an extension; an `IOError` (WL3011) where it is not there, WL3001 where it does not compile, its errors in the message |
| `worker.post(value)` | copies `value` to the worker, giving `null`; dropped where the worker has ended |
| `await worker.receive()` | the next value the worker posted; `null` once it has ended and all it posted is received; the exception it ended with thrown, once |
| `worker.dispose()` | ends the worker, as a request past its time is ended, its `finally` blocks not run; also at the end of a `using` |
| `parent` | in a worker's module, its `Worker`'s end, with `post(value)` and `receive()` as the `Worker`'s; `null` in a program not run as a worker |
| `await workers.run(path, name, arguments)` | the function `name` the module at `path` exports run on a worker of the module's pool, given `arguments`, an array, or `null` for none, each copied there; what it gives copied back; its exception thrown here |

A value crosses copied as it was: `null`, Booleans, integers, numbers, decimals, strings, bytes, dates, and arrays,
maps and sets of them, the references they share and their cycles kept; anything else, an instance, a function, a
class, a task, is WL3001, naming it. An error, an instance of `Error` or of a class extending it, crosses as an `Error`,
its `message` and its `code` as they were, with two fields of its own, `className`, its class's name, and `thrownAt`,
where it was thrown.

A worker ends once its module's top level has ended and none of its tasks waits, a task waiting for its parent's next
value among them; when its `Worker` is disposed; and when its program ends, a `Worker` let go of not ending it. An
exception nothing catches in it is thrown by its parent's next `receive()`, copied as an error is. One its parent never
receives makes the program's run fail with it when the program ends, exit code 3, reported where the worker threw it, as a
task's exception nothing awaits does; a worker's own run fails so in turn, its parent receiving the error. A program
that does not care what a worker ended with disposes of it, which gives the error up.

Save this worker as `square.wlg`:

```javascript
import { parent } from "std:workers";

// Each number its parent posts answered with its square, until the parent posts null.
var n = await parent.receive();
while (n != null) {
    parent.post(n * n);
    n = await parent.receive();
}
```

And this program beside it, which runs it:

```javascript
import { writeLn } from "std:console";
import { Worker } from "std:workers";

using (var worker = new Worker("square.wlg")) {
    foreach (var n in [3, 4, 5]) worker.post(n);
    writeLn([await worker.receive(), await worker.receive(), await worker.receive()]);
    worker.post(null);
    writeLn(await worker.receive());
}
```

```text
[9, 16, 25]
null
```

A worker's exception, with `fails.wlg` holding `class NotFound extends Error {}` and `throw new NotFound("no such
tea");` on its second line:

```javascript
import { writeLn } from "std:console";
import { Worker } from "std:workers";

try {
    await new Worker("fails.wlg").receive();
} catch (e) {
    writeLn([e is Error, e.className, e.message, e.thrownAt]);
}
```

```text
[true, "NotFound", "no such tea", "fails.wlg(2,1)"]
```

A module's pool runs its exported functions, `import workers from "std:workers";`, or `import { run } from
"std:workers";`: its workers are made as calls come, as many as the machine's processors at most, each running the
module once, then the calls it is given, one at a time; a call beyond them waits for the first that is free, and the
workers end with the program. A name the module does not export is a `MemberError` (WL3006), and arguments a function
does not take an `ArgumentError` (WL2006), where `run` is called. With `primes.wlg`:

```javascript
// How many primes there are below a number, counted the slow way.
export function count(below) {
    var found = 0;
    for (var n = 2; n < below; n++) {
        var prime = true;
        for (var d = 2; d * d <= n && prime; d++) prime = n % d != 0;
        if (prime) found++;
    }
    return found;
}
```

four counts at once, each on a processor of its own:

```javascript
import { writeLn } from "std:console";
import workers from "std:workers";

var counts = [];
foreach (var below in [10000, 20000, 30000, 40000]) counts.push(workers.run("primes.wlg", "count", [below]));
writeLn(await Task.all(counts));
```

```text
[1229, 2262, 3245, 4203]
```

A script's `weblingo.config` limits its workers in `"limits"`, as a site's `wasp.config` limits its requests: `{
"limits": { "workers": 8, "workerTimeout": 60, "workerInstructions": 1000000000, "workerMemory": 128 } }`, each optional.
`"workers"` is how many may run at once, the script's own, its workers' and its pools' together, from 1 to 1,024, the
machine's processors by default: a `new Worker` beyond them is WL3001 where it is made, and a pool's call waits for one
to end. Each worker's seconds, `"workerTimeout"`, a pool's worker's for each call, its instructions, `"workerInstructions"`,
and its megabytes of strings, `"workerMemory"`, are none, none and 128 by default. A worker past one is stopped, and
its parent's next `receive()`, or the pool's call, throws an `Error` whose code is WL3003, its message saying which.

## 34. `std:shared`

Memory a site's requests share, or, under `weblingo`, a program and its workers: `import { Shared } from "std:shared";`.
Every request of a site has its own heap and shares nothing with the others, so that a site keeps what all of them read
or count here: a store of values by key, kept by the host outside every request's heap. A value is copied in and out,
as a worker's message is, so that what one request reads is a copy no other can change: `null`, Booleans, numbers,
decimals, strings, bytes, dates, and arrays, maps and sets of them; anything else, an instance, an error, a function, is
WL3001, naming it. A key is a string of at most 1,024 characters.

| Member | What it does |
|---|---|
| `Shared.get(key)` | a copy of the value kept under `key`; `null` where there is none |
| `Shared.set(key, value)` | keeps a copy of `value` under `key`, in place of what was, until it is removed; `null` |
| `Shared.set(key, value, seconds)` | the same, kept for that many seconds, a number more than 0, then gone |
| `Shared.remove(key)` | removes what `key` keeps, giving whether it kept anything |
| `Shared.add(key, n)` | adds the integer `n` to the integer `key` keeps, or to 0 where it keeps nothing, giving the total; WL3001 where it keeps another kind of value, an `OverflowError`, WL3002, past int64 |
| `Shared.take(key)` | the value `key` keeps, which it then keeps no more; `null` where there is none |

`add` and `take` are each one step, which no other request can come between, so that two requests that add at once
both count, and a value taken is taken once. A site's store holds at most 64 MB of copied values, a key's characters and
a little for each entry counted too, unless its `wasp.config` says otherwise, `"limits": { "shared": 16 }`, in megabytes,
or `null` for no most; under `weblingo`, `weblingo.config`'s. A `set`, or an `add` of a new key, past it is a
`SharedMemoryError`, WL3025, which a script may catch, nothing then kept. A site's store is kept while the site
reloads, and lost only when its process ends: a site that changes what a key means uses a new key. Each of a site's
processes has a store of its own, where it runs as several (the WASP User Manual's section 14).

```javascript
import { writeLn } from "std:console";
import { Shared } from "std:shared";

Shared.set("greeting", { text: "hello", times: [1, 2] });
var greeting = Shared.get("greeting");
greeting["text"] = "changed";
writeLn(Shared.get("greeting")["text"]);
writeLn([Shared.add("views", 1), Shared.add("views", 1)]);
writeLn([Shared.take("views"), Shared.get("views")]);
```

```text
hello
[1, 2]
[2, null]
```
