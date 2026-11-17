# WebLingo Language Reference

WebLingo is a general-purpose scripting language, embeddable in other programs, and the language of WASP, WebLingo's
server pages. Its syntax will look familiar to anyone who knows JavaScript, C# or Java, but it is a language of its own,
not JavaScript: it is stricter. Nothing converts implicitly, a condition must be a Boolean, a variable always has a
value, and integers do not silently overflow.

This reference describes the language as it is built today, which is pre-alpha: what it says works, and every example
in it was run with `weblingo`, its output shown as it came. What the language does not have yet is listed at the end,
in [What is not in the language yet](#17-what-is-not-in-the-language-yet). Pages, which put WebLingo into HTML, are the
WASP User Manual's (`Documents/wasp-user-manual.md`). Every built-in class's members, and the standard modules, are
listed in the WebLingo Standard Library Reference (`Documents/weblingo-standard-library-reference.md`).

An example is a program, followed by what it writes:

```javascript
writeLn("Hello, WebLingo!");
```

```text
Hello, WebLingo!
```

## Contents

1. [Running programs](#1-running-programs)
2. [Lexical structure](#2-lexical-structure)
3. [Variables and scope](#3-variables-and-scope)
4. [Values](#4-values)
5. [Operators](#5-operators)
6. [Strings](#6-strings)
7. [Statements](#7-statements)
8. [Functions](#8-functions)
9. [Collections](#9-collections)
10. [Classes](#10-classes)
11. [Errors and exceptions](#11-errors-and-exceptions)
12. [Disposal](#12-disposal)
13. [Asynchrony](#13-asynchrony)
14. [Modules](#14-modules)
15. [The standard library](#15-the-standard-library)
16. [Diagnostic codes](#16-diagnostic-codes)
17. [What is not in the language yet](#17-what-is-not-in-the-language-yet)

---

## 1. Running programs

A WebLingo program is a `.wl` file, a *script*, which `weblingo` runs:

```bash
weblingo run hello.wl
```

| Command | What it does |
|---|---|
| `weblingo run <file.wl> [name=value …]` | compiles the script and runs it, giving its parameters the values after it |
| `weblingo check <file.wl>` | compiles the script and reports its errors, without running it |
| `weblingo compile [--register \| --stack] <file.wl>` | compiles the script into bytecode, a `.wlbc` file beside it |
| `weblingo run <file.wlbc>` | runs compiled bytecode without compiling anything |
| `weblingo disasm <file.wlbc>` | shows compiled bytecode as text |
| `weblingo --version` | its version |

`weblingo run` of a `.wl` compiles it in memory, and writes no file; `weblingo compile` writes one, for the register VM
by default, or for the stack VM with `--stack`. `weblingo disasm hello.wlbc` shows what the compiler made:

```text
function main (0 locals, 1 registers)
00  LoadConstant r0, "Hello, WebLingo!"
01  CallBuiltin r0, writeLn(r0)
02  Return
```

An error is written to the error output as `file(line,column): error CODE: message`, and the exit code says what
happened:

| Exit code | Meaning |
|---|---|
| 0 | it ran, or compiled, without errors |
| 1 | the script has compilation errors |
| 2 | the command line was wrong |
| 3 | the script failed while it ran |
| 4 | a file could not be read or written |

```text
vars-const.wl(2,1): error WL2003: 'limit' is a constant and cannot be assigned.
```

A script's own parameters ([section 14](#script-parameters)) are given after its name, each a string. A
`weblingo.config` file in the script's folder configures where native modules are found
([section 15](#native-modules)).

Editors can colour WebLingo's files: the repository's `Editors/` folder has syntax colouring for `.wl` files, and for
WASP's `.wasp` pages, and its `README.md` says how to install it.

---

## 2. Lexical structure

- **Statements end with `;`.** There is no automatic semicolon insertion. A block, `{ … }`, has none after its `}`.
- **Comments.** `//` runs to the end of the line; `/* … */` may span lines, and does not nest.
- **Names** are ASCII letters, digits and `_`, not starting with a digit, and case matters: `count` and `Count` are two
  names.
- **Numbers** are written in decimal, `42`, `3.5`, `1e21` and the decimal `1.5m`; an integer in hexadecimal too,
  `0x1F`, or in binary, `0b1010`, the prefix's letter and the digits in either case. A `_` may stand between two
  digits, one at a time, to group them, and means nothing: `1_000_000`, `0.000_1`, `0xFF_FF`.
- **Whitespace and line breaks** separate tokens and otherwise mean nothing. A file may use any line ending.
- **Keywords**, which cannot be names: `async`, `await`, `break`, `catch`, `class`, `const`, `continue`, `do`, `else`,
  `export`, `extend`, `extends`, `false`, `finally`, `for`, `foreach`, `function`, `if`, `import`, `Infinity`, `is`,
  `let`, `NaN`, `new`, `null`, `param`, `return`, `static`, `super`, `this`, `throw`, `true`, `try`, `using`, `var`,
  `while`. `let` is reserved and never declares anything. A few words mean something only where they stand: `in` in a
  `foreach`, `from` in an `import`, `property`, `get`, `set` and `field` in a class's property, and `operator` in a
  class's operator.

```javascript
// A comment runs to the end of its line.
/* A block comment
   may span lines. */
var answer = 42; // after a statement too
writeLn(answer);
```

```text
42
```

---

## 3. Variables and scope

`var` declares a variable, and `const` a constant, which cannot be assigned again:

```javascript
var count = 1;
const limit = 3;
count = count + 1;
writeLn(count);
writeLn(limit);
```

```text
2
3
```

- **Every declaration needs a value**: `var x;` is an error (WL1006), so a variable always has one. There is no
  `undefined`; `null` is the value for "nothing".
- **Assigning a constant** is an error when the script compiles (WL2003). `const` does not freeze what it refers to: a
  constant array can still have elements added.
- **Scope is the block.** A variable exists from its declaration to the end of the block, `{ … }`, that declares it,
  as JavaScript's `let` does. An inner block may declare a name an outer one has, hiding it there:

  ```javascript
  var x = "outer";
  {
    var x = "inner";
    writeLn(x);
  }
  writeLn(x);
  ```

  ```text
  inner
  outer
  ```

- **Declaring a name twice in one block** is an error (WL2002).
- **A declaration takes effect where it stands.** Before it, even in its own block, the name means what it means
  outside:

  ```javascript
  var x = 1;
  {
    writeLn(x);
    var x = 2;
    writeLn(x);
  }
  ```

  ```text
  1
  2
  ```

- **There are no implicit globals**: a name that nothing declares is an error (WL2001), when the script compiles.

---

## 4. Values

| Kind | Examples | Class |
|---|---|---|
| null | `null` | none |
| Boolean | `true`, `false` | `Boolean` |
| Integer, 64 bits | `42`, `-7`, `9223372036854775807`, `0xFF`, `0b1010` | `Integer` |
| Number, binary64 | `3.5`, `1e21`, `2.5E-3`, `Infinity`, `NaN` | `Number` |
| String | `"tea"`, `'tea'` | `String` |
| Array | `[1, "two", null]` | `Array` |
| Map | `{ name: "Ann", age: 30 }` | `Map` |
| Set | `#{1, 2, 3}` | `Set` |
| Bytes | `Bytes.from("hi")`, `Bytes.from([104, 105])` | `Bytes` |
| Decimal | `1.5m`, `0.1m` | `Decimal` |
| Regular expression | `new Regex("a+b", "i")` | `Regex` |
| Date and time | `DateTime.now()`, `DateTime.utc(2026, 11, 16)` | `DateTime` |
| Function | `function (x) { … }`, `x => x * 2` | `Function` |
| Class | `class Point { … }` | `Class` |
| Instance | `new Point(1, 2)` | its class |
| Task | what an `async` function gives | `Task` |

`writeLn` writes each kind as its text:

```javascript
writeLn(null);
writeLn(true);
writeLn(42);
writeLn(3.5);
writeLn(1e21);
writeLn(5.0);
writeLn("text");
writeLn([1, "two", null]);
writeLn({ name: "Ann", age: 30 });
writeLn(#{1, 2, 3});
```

```text
null
true
42
3.5
1E+21
5.0
text
[1, "two", null]
{"name": "Ann", "age": 30}
#{1, 2, 3}
```

- **Integers** are 64-bit, written in decimal digits. A result that does not fit is an error, not a wrapped number
  (WL3002).
- **Numbers** are binary64 (IEEE 754 doubles), and are always written with a decimal point or an exponent, `5.0`, so
  that a number never looks like an integer.
- **No value converts to another by itself.** A string and a number are not joined, and a condition must be a Boolean.
  `toString()`, every value's, gives its text, and `Integer.parse`, `Number.parse` and `Boolean.parse` read one:

  ```javascript
  writeLn("n = " + 5.toString());
  writeLn("n = " + (2.5).toString());
  writeLn(Integer.parse("10") + 1);
  writeLn(Number.parse("1.5") * 2);
  writeLn(Boolean.parse("true"));
  ```

  ```text
  n = 5
  n = 2.5
  11
  3.0
  true
  ```

- **Every value but `null` has a class** ([section 10](#classes-of-values)), so `5 is Integer` and `"a" is String`
  are true.

### Decimals

A decimal is a number written with an `m`, `2.50m`, whose arithmetic is exact in decimal digits, as money needs:

```javascript
var price = 2.50m;
var total = price * 3 + 0.10m;
writeLn(total);
writeLn(0.1m + 0.2m);
writeLn(0.1 + 0.2);
writeLn($"{total:0.00}");
writeLn([7m / 2, 1m == 1, new Decimal(0.1), new Number(total)]);
```

```text
7.6m
0.3m
0.30000000000000004
7.60
[3.5m, true, 0.1m, 7.6]
```

- **A decimal and an integer** mix: the integer converts exactly, `1.5m + 1` is `2.5m`.
- **A decimal and a number do not**: `1.5m + 1.5` is an error. Convert one, `new Decimal(x)` or `new Number(d)`.
- **Its text** has its fewest digits and its `m`: `2.50m` is written `2.5m`. A format writes it as you choose, `{d:0.00}`.
- **`Decimal.parse("2.50")`** reads one; `new Integer(d)` takes only a whole one.
- **Its range** is .NET's `decimal`'s, 28 or 29 digits; beyond it is an error, as is dividing a decimal by 0.

### Dates and times

A date and time is a value of the class `DateTime`: an instant, to the millisecond, with its offset from UTC, the time on
the clocks where it was read. Like a string, it is immutable; its years are 1 to 9999.

```javascript
var d = DateTime.utc(2026, 11, 16, 14, 30);
writeLn(d);
writeLn([d.year, d.month, d.day, d.hour, d.minute, d.dayOfWeek]);
writeLn(d.addDays(1).addHours(-2.5));
writeLn(DateTime.utc(2026, 1, 31).addMonths(1));
var meeting = DateTime.parse("2026-11-16T15:30:00+01:00");
writeLn(meeting);
writeLn([meeting == d, meeting - d, meeting.toUtc()]);
writeLn(d.format("dddd, d MMMM yyyy 'at' HH:mm"));
writeLn($"{d:yyyy-MM-dd} {d:HH:mm}");
writeLn(DateTime.parse("16/11/2026 14:30 +00:00", "dd/MM/yyyy HH:mm zzz") == d);
```

```text
DateTime:2026-11-16T14:30:00Z
[2026, 11, 16, 14, 30, 1]
DateTime:2026-11-17T12:00:00Z
DateTime:2026-02-28T00:00:00Z
DateTime:2026-11-16T15:30:00+01:00
[true, 0, DateTime:2026-11-16T14:30:00Z]
Monday, 16 November 2026 at 14:30
2026-11-16 14:30
true
```

- **Making one.** `DateTime.now()` is the server's time, with the server's offset, and `DateTime.utcNow()` the same
  instant in UTC. `DateTime.utc(year, month, day, hour, minute, second, millisecond)`, and `DateTime.local(…)`, in the
  server's time zone, make one from its parts, those after the day optional, 0 when left out; months and days count
  from 1. `DateTime.fromUnixMilliseconds(ms)` makes one from milliseconds since 1970-01-01T00:00:00Z, in UTC.
  `new DateTime()` is an error.
- **Reading one.** `d.year`, `d.month`, `d.day`, `d.hour`, `d.minute`, `d.second` and `d.millisecond` are its time on
  its own clocks; `d.dayOfWeek` is 0 for Sunday to 6 for Saturday; `d.dayOfYear` counts from 1; `d.offset` is how many
  minutes ahead of UTC it is; and `d.unixMilliseconds` its milliseconds since 1970-01-01T00:00:00Z.
- **Moving one.** `d.addYears(n)` and `d.addMonths(n)` take an integer, and keep the day, or the month's last when it
  has fewer; `d.addDays(n)`, `d.addHours(n)`, `d.addMinutes(n)`, `d.addSeconds(n)` and `d.addMilliseconds(n)` take a
  number, rounded to the millisecond. Each gives a new date with the same offset. `d.toUtc()` is the same instant in
  UTC, and `d.toLocal()` in the server's time zone.
- **Comparing them.** `==`, `!=`, `<`, `<=`, `>` and `>=` compare instants, whatever their offsets, and `b - a` is the
  milliseconds from `a` to `b`. `+` does not add to a date: its `add` methods do. Dates are not keys of a map.
- **As text.** `write` writes `DateTime:` and the date as ISO 8601 writes it, its milliseconds only when it has any.
  `d.format(format)`, and an interpolation's format, `{d:yyyy-MM-dd}`, take .NET's date and time formats, standard
  (`"o"`, `"d"`) or custom (`"dddd, d MMMM yyyy"`), in English, whatever the server's language.
- **From text.** `DateTime.parse(text)` reads ISO 8601: `2026-11-16T14:30:00Z`, `2026-11-16T15:30:00+01:00`, and,
  in the server's time zone, `2026-11-16T14:30` and `2026-11-16`. `DateTime.parse(text, format)` reads by a format of
  .NET's, a time without a zone being the server's.
- **Time zones.** Besides UTC and the server's own, any zone the server knows, by its IANA name:
  `d.toZone("Europe/Warsaw")` is the same instant on Warsaw's clocks, and
  `DateTime.inZone("America/New_York", 2026, 11, 16, 9, 30)` makes a date from its parts there, as
  `DateTime.local(…)` makes one in the server's zone:

```javascript
var call = DateTime.inZone("America/New_York", 2026, 11, 16, 9, 30);
writeLn(call);
writeLn(call.toZone("Europe/Warsaw"));
writeLn(call.toZone("Asia/Kolkata").format("HH:mm"));
writeLn(call.toUtc() == call);
```

```text
DateTime:2026-11-16T09:30:00-05:00
DateTime:2026-11-16T15:30:00+01:00
20:00
true
```

  The zones are the server's, Windows' or its operating system's, and may differ from one server to another; a
  name it does not know is an error. A date keeps its offset, not its zone: moved past a change of its zone's
  clocks, it keeps the offset it had, until `toZone` again. A time a zone's clocks skip, or have twice, takes the
  zone's standard offset.

---

## 5. Operators

| Operators | What they do |
|---|---|
| `-x`, `+x` | negation, and the number itself |
| `a * b`, `a / b` | multiplication and division |
| `a + b`, `a - b` | addition and subtraction; `+` also joins two strings |
| `a < b`, `a <= b`, `a > b`, `a >= b` | comparisons of numbers |
| `a == b`, `a != b` | equality |
| `!a` | not |
| `a && b` | and, short-circuit |
| `a \|\| b` | or, short-circuit |
| `x = value` | assignment, whose value is the value assigned |

Precedence, from the tightest: unary `- + ! ++ --`; `* / %`; `+ -`; `< <= > >=`; `== !=`; `&&`; `||`; `??`; `? :`; and
assignment, which
groups to the right (`a = b = 3`). The binary operators group to the left. Parentheses group as usual.

### Arithmetic

```javascript
writeLn(7 / 2);
writeLn(8 / 2);
writeLn(1 + 0.5);
writeLn(2 * 3 + 4);
writeLn(2 * (3 + 4));
writeLn(1 / 0);
writeLn(0 / 0);
writeLn(-Infinity);
writeLn(0.1 + 0.2);
```

```text
3.5
4.0
1.5
10
14
Infinity
NaN
-Infinity
0.30000000000000004
```

- **`/` always gives a number**: `7 / 2` is `3.5` and `8 / 2` is `4.0`; there is no integer division.
- **`%` is the remainder**, as tight as `*` and `/`, its sign the dividend's: `7 % 3` is `1`, `-7 % 3` is `-1`, and
  `5.5 % 2` is `1.5`. An integer's remainder by 0 is an error, WL3013 (`DivisionByZeroError`); a number's is `NaN`.
- **An integer meeting a number** becomes a number: `1 + 0.5` is `1.5`.
- **Integers do not overflow silently**:

  ```javascript
  writeLn(9223372036854775807 + 1);
  ```

  ```text
  values-overflow.wl(1,9): error WL3002: 9223372036854775807 + 1 does not fit in a 64-bit integer.
  ```

- **Numbers follow IEEE 754**: `1 / 0` is `Infinity`, `0 / 0` is `NaN`, and `0.1 + 0.2` is what it always is.
- **Nothing else is arithmetic**: `"n = " + 5` is an error, not `"n = 5"`:

  ```text
  values-noconvert.wl(1,9): error WL3001: '+' joins two strings or adds two numbers, not a string and the integer 5.
  ```

### Equality

`==` and `!=` never convert: values of different kinds are simply unequal, except that an integer and a number compare
as numbers. Strings compare their text; arrays, maps, sets, functions and instances are equal only to themselves.

```javascript
writeLn(1 == 1.0);
writeLn("a" == "a");
writeLn(1 == "1");
writeLn(null == null);
writeLn([1] == [1]);
var a = [1];
writeLn(a == a);
writeLn(NaN == NaN);
```

```text
true
true
false
true
false
true
false
```

There is no `===`; `==` is already strict. A class may give its instances a meaning of equality of their own with an
`equal` method ([section 10](#equality-and-equal)).

### Comparisons and logic

`<`, `<=`, `>` and `>=` compare two numbers, two strings or two dates. Strings compare by their characters' Unicode
values, never by language, so that `"B" < "a"`; `s.compare(other)` gives -1, 0 or 1 by the same order. `!`, `&&` and
`||` take Booleans only, and so does every condition: there is
no "truthiness".

```javascript
var age = 20;
writeLn(age >= 18 && age < 65);
writeLn(!(age > 30) || false);
```

```text
true
true
```

```javascript
if (1) writeLn("never");
```

```text
values-truthy.wl(1,5): error WL3001: A condition needs a Boolean, not the integer 1.
```

### Assignment's operators

```javascript
var total = 10;
total += 5;
total *= 2;
writeLn(total);
var counts = { tea: 1 };
counts["tea"] += 1;
writeLn(counts);
var i = 0;
writeLn([i++, i, ++i, i]);
var name = null;
name ??= "guest";
writeLn(name);
```

```text
30
{"tea": 2}
[0, 1, 2, 2]
guest
```

- **`x += v`**, and `-=`, `*=`, `/=` and `%=`, assign `x + v`, and so on; **`x ??= v`** assigns `v` only when `x` is
  `null`.
- **`++` and `--`** add or subtract 1: `++i` gives the new value, `i++` the old one.
- **Each is an expression**, giving the value assigned, so that `y = x += 1` assigns both.
- **The target is evaluated once**: `a[next()] += 1` calls `next` once. A target is a variable, an element or an entry,
  `a[i]`, or a member, `x.name`; a constant cannot be one.

### Choosing a value: `? :`, `if` and `??`

```javascript
var stock = 0;
writeLn(stock > 0 ? "in stock" : "sold out");
var size = if (stock > 10) "many" else "few";
writeLn(size);
var user = null;
writeLn(user?.name ?? "stranger");
writeLn($"{(stock == 1 ? "item" : "items")}");
```

```text
sold out
few
stranger
items
```

- **`c ? a : b`** and **`if (c) a else b`** are the same: `a` when `c` is `true`, `b` when it is `false`, and only that
  one evaluated. The condition must be a Boolean. An `if` that gives a value needs its `else`.
- **`a ?? b`** is `a`, unless it is `null`, and then `b`, which is evaluated only then.
- **`?[` written together is always an optional index**: write `c ? [1] : [2]`, with a space, for a conditional.
- **In an interpolation**, put a conditional in parentheses, `{(c ? a : b)}`: its `:` would otherwise begin a format.

---

## 6. Strings

A string is immutable Unicode text, written in double or single quotes, which mean the same, on one line.

```javascript
var s = "héllo";
writeLn(s.length);
writeLn(s[1] == "é");
writeLn("tea" + " " + "time");
writeLn('single' == "single");
writeLn("tab:\tend");
writeLn("\u{1F375}".length);
```

```text
5
true
tea time
true
tab:	end
1
```

- **`length` and indexing count Unicode characters** (scalar values), not bytes: `"héllo".length` is 5, and the emoji
  `"\u{1F375}"` is one. `s[i]` is the character at `i`, from 0, as a string of one.
- **Escapes**: `\n`, `\r`, `\t`, `\0`, `\\`, `\'`, `\"`, and `\u{…}`, a Unicode character by its hexadecimal number.
  Any other is an error (WL1009).
- **`+` joins two strings**, and only strings.
- **`==` compares text**: `"tea" == "tea"` is true.
- A string cannot be changed: `s[0] = "H"` is an error.

### String methods

```javascript
var s = "Green tea, black tea";
writeLn([s.indexOf("tea"), s.lastIndexOf("tea"), s.contains("black"), s.startsWith("Green")]);
writeLn(s.slice(6, 9));
writeLn(s.split(", "));
writeLn(["  tea  ".trim(), s.toUpper(), s.replace("tea", "coffee")]);
writeLn(["ab".repeat(3), "7".padStart(3, "0"), ", ".join(["a", 1, true])]);
```

```text
[6, 17, true, true]
tea
["Green tea", "black tea"]
["tea", "GREEN TEA, BLACK TEA", "Green coffee, black coffee"]
["ababab", "007", "a, 1, true"]
```

- **Their names** are C#'s, and JavaScript's and C#'s others work too: `includes` is `contains`, `toUpperCase` and
  `toLowerCase` are `toUpper` and `toLower`, `replaceAll` is `replace`, and `padLeft` and `padRight` are `padStart` and
  `padEnd`.
- **Positions count Unicode characters**, as indexing does: `indexOf` and `lastIndexOf` give one, or -1;
  `indexOf(search, from)` starts at `from`; `slice(start, end)` takes the characters from `start` to before `end`, `end`
  left out its end, and a range outside the string is an error.
- **Searching is exact**, character by character, never by the server's language.
- **`split(separator)`** keeps empty parts, `"a,,b".split(",")` giving `["a", "", "b"]`; an empty separator gives
  each character.
- **Case** changes the same way on every server, character by character: `"Straße".toUpper()` is `"STRAßE"`.
- **`replace(old, new)`** replaces every `old`, not only the first.
- **`padStart(width, pad)` and `padEnd`** pad to a width in characters, with a space unless a pad is given.
- **`separator.join(array)`** joins strings, numbers, Booleans, decimals and dates, `null` as nothing; give anything
  else's text first, `x.toString()`.

### Regular expressions

A regular expression is made by the class `Regex`, from a pattern in .NET's syntax and flags:

```javascript
var email = new Regex("(\\w+)@(\\w+)\\.com");
var text = "ann@tea.com, bob@cake.com";
writeLn(email.test(text));
writeLn(email.match(text));
writeLn(email.matchAll(text).length);
writeLn(text.replace(email, "$2:$1"));
writeLn(text.split(new Regex(",\\s*")));
writeLn(new Regex("^tea$", "im").test("Green\nTEA"));
```

```text
true
["ann@tea.com", "ann", "tea"]
2
tea:ann, cake:bob
["ann@tea.com", "bob@cake.com"]
true
```

- **The flags**, a string, are `i`, ignoring case, `m`, `^` and `$` at every line, `s`, `.` matching a line break too,
  `x`, white space in the pattern ignored, and `n`, only named groups captured.
- **`r.match(s)`** gives the first match, an array of its groups' text, the whole match first, a group that matched
  nothing `null`; or `null`, for no match. `r.matchAll(s)` gives every match so, in an array; `r.test(s)` whether there
  is one. Other names work too: `isMatch` for `test`, `exec` for `match`, `matches` for `matchAll`.
- **Replacing and splitting**: `s.replace(r, replacement)` or `r.replace(s, replacement)` replaces every match, `$1` in
  the replacement standing for the first group, `$0` for the whole match; `s.split(r)` or `r.split(s)` splits at each.
- **A pattern is written in a string**, so its backslashes are doubled: `"\\d+"` is the pattern `\d+`.
- **A pattern that takes more than a second** to match, as some do on some text, stops with a `TimeoutError`.

### JSON

```javascript
var order = Json.parse("{\"tea\": \"Sencha\", \"cups\": 2, \"price\": 6.5, \"extras\": [\"milk\"]}");
writeLn([order["tea"], order["cups"] is Integer, order["price"] is Number, order["extras"][0]]);
writeLn(Json.stringify({ tea: "Assam", cups: 1, paid: true, note: null }));
class Tea {
  constructor(name, price) { this.name = name; this.price = price; }
}
class Money {
  constructor(amount) { this.amount = amount; }
  toJson() { return $"{this.amount} EUR"; }
}
writeLn(Json.stringify([new Tea("Sencha", 6.5), new Money(4.25)]));
writeLn(Json.stringify({ list: [1, 2] }, 2));
```

```text
["Sencha", true, true, "milk"]
{"tea":"Assam","cups":1,"paid":true,"note":null}
[{"name":"Sencha","price":6.5},"4.25 EUR"]
{
  "list": [
    1,
    2
  ]
}
```

- **`Json.parse(text)`** gives a map for an object, an array for an array, and strings, `true`, `false` and `null`;
  a number is an integer when it has no fraction or exponent and fits 64 bits, else a number. Text that is not JSON
  is an error, which names where.
- **`Json.stringify(value, indent)`** writes maps, arrays, sets, strings, numbers, decimals, Booleans, `null`, and
  dates, as their ISO text; an instance as its fields, or as what its class's `toJson()` gives. `indent`, 0 unless
  given, up to 10, puts each element on a line of its own. A function, or a value that contains itself, is an error.

### Multi-line strings

Three quotes, `"""` or `'''`, begin a string that may span lines. It is raw: `\` is an ordinary character. The spaces
before the closing quotes' column are removed from every line, so that the string can be indented with the code:

```javascript
var poem = """
    Green tea,
      steeped slow.
    """;
writeLn(poem);
```

```text
Green tea,
  steeped slow.
```

### Interpolation

A string marked with `$` puts the values of the expressions in braces into its text, as `write` writes them, `null` as
nothing:

```javascript
var name = "Ann";
var price = 6.5;
writeLn($"Hello, {name}! Tea is {price} today.");
writeLn($"{price:N2} {price:0.000} {255:X4} {1234567:N0}");
writeLn($"[{name,6}] [{name,-6}]");
writeLn($"Braces: {{ and }}");
writeLn($"{1 + 2} {null}|");
```

```text
Hello, Ann! Tea is 6.5 today.
6.50 6.500 00FF 1,234,567
[   Ann] [Ann   ]
Braces: { and }
3 |
```

- **A format** follows a colon: a .NET numeric format, standard (`N2`, two decimals with thousands separators; `F2`,
  two decimals; `D5`, five digits; `E3`, exponent; `X4`, hexadecimal) or custom (`0.000`, `#,##0`), for integers and
  numbers only, never depending on the machine's culture.
- **An alignment** follows a comma, before any format: `{name,6}` pads to six characters on the left, `{name,-6}` on the
  right.
- **A brace** is written doubled, `{{` and `}}`, or escaped, `\{` and `\}`.
- A multi-line string may be interpolated too, `$"""…{name}…"""`; there, a brace is written as it is, and as many `$` as
  the braces that open an interpolation mark it: `$$"""{ "name": {{name}} }"""`.

---

## 7. Statements

### `if`

```javascript
var n = 7;
if (n > 5) {
  writeLn("big");
} else if (n > 2) {
  writeLn("medium");
} else {
  writeLn("small");
}
```

```text
big
```

A branch may be a single statement or a block, and has a scope of its own either way. An `else` belongs to the nearest
`if` without one.

### Loops

```javascript
var i = 0;
while (i < 3) { write(i); i = i + 1; }
writeLn("");
for (var j = 0; j < 3; j = j + 1) write(j);
writeLn("");
var k = 0;
do { write(k); k = k + 1; } while (k < 3);
writeLn("");
for (var n = 0; n < 10; n = n + 1) {
  if (n == 2) continue;
  if (n == 5) break;
  write(n);
}
writeLn("");
```

```text
012
012
012
0134
```

- `while (condition) statement`, `do statement while (condition);` and `for (init; condition; step) statement`, as in C.
  A `for`'s parts may each be left out; a missing condition is `true`.
- A `for`'s `var` belongs to the loop, and each turn of the loop has its own, so that a closure made in a turn sees that
  turn's value ([section 8](#closures)).
- `break;` leaves the innermost loop, and `continue;` goes on to its next turn. Either outside a loop is an error
  (WL2007).

### `switch`

```javascript
class Tea { constructor(name) { this.name = name; } }
function describe(item) {
  switch (item) {
    case 1, 2 { return "a few"; }
    case "water" { return "just water"; }
    case Tea t { return "a tea: " + t.name; }
    default { return "something else"; }
  }
}
writeLn(describe(2));
writeLn(describe(new Tea("Sencha")));
writeLn(describe(true));
var cups = 3;
writeLn(cups switch { 0 => "none", 1 => "one cup", _ => $"{cups} cups" });
```

```text
a few
a tea: Sencha
something else
3 cups
```

- **A case** lists values, matched by `==`, `case 1, 2`, or a class and a name for the value, `case Tea t`, which
  matches an instance of `Tea`, or of a class extending it, as a typed `catch` does.
- **The first case that matches runs**, its block only; `default` runs when none does. No case runs into the next,
  and none needs a `break`: a `break` or a `continue` in a case acts on the loop around the `switch`, as in an `if`.
- **A `switch` expression**, `value switch { … }`, gives the result of its first arm that matches; it needs a
  `_ => …` arm, for any other value. Its patterns are a case's.

### `foreach`

`foreach (var item in collection)` visits a collection's items:

```javascript
foreach (var tea in ["Sencha", "Assam"]) writeLn(tea);
foreach (var c in "abc") write(c + ".");
writeLn("");
foreach (var (name, price) in { sencha: 6.5 }) writeLn($"{name}: {price}");
var total = 0;
foreach (var n in #{1, 2, 3}) total = total + n;
writeLn(total);
```

```text
Sencha
Assam
a.b.c.
sencha: 6.5
6
```

- An array's elements are visited in order, a string's characters as strings of one, and a set's values.
- A map's entries are visited as a key and a value, `foreach (var (key, value) in map)`.
- An instance of a class is visited by its `iterate()` method ([section 10](#iterating-an-instance)).
- A collection changed while it is visited is not an error: the loop sees the change.

---

## 8. Functions

```javascript
writeLn(add(2, 3));
function add(a, b) { return a + b; }
function greet(name, greeting = "Hello") { return $"{greeting}, {name}!"; }
writeLn(greet("Ann"));
writeLn(greet("Ann", "Hi"));
function nothing() { }
writeLn(nothing());
```

```text
5
Hello, Ann!
Hi, Ann!
null
```

- **A function declaration is in scope throughout its block**, so functions can call each other in any order, and a
  function can be called above its declaration. Its name is a constant.
- **`return value;`** ends it with a value; `return;`, or the end of its body, gives `null`.
- **A parameter may have a default**, evaluated each time it is left out; parameters with defaults come last.
- **A call with the wrong number of arguments** is an error, when the script compiles for a function the call names:

  ```javascript
  function f(a, b = 1) { return a + b; }
  writeLn(f(1, 2, 3));
  ```

  ```text
  fn-arity.wl(2,9): error WL2006: 'f' takes 1 to 2 arguments, not 3.
  ```

- **Recursion** is bounded by the call depth an execution allows, 10,000 by default (WL3003), not by the machine's
  stack:

  ```javascript
  function factorial(n) { if (n <= 1) return 1; return n * factorial(n - 1); }
  writeLn(factorial(20));
  ```

  ```text
  2432902008176640000
  ```

### Rest parameters

A function's last parameter may be written `...name`: an array of the arguments after the others, so that the
function takes any number of them.

```javascript
function sum(label, ...numbers) {
  var total = 0;
  foreach (var n in numbers) total += n;
  return $"{label}: {total}";
}
writeLn(sum("none"));
writeLn(sum("three", 1, 2, 3));
var pair = (first, ...others) => [first, others];
writeLn(pair("a", "b", "c"));
```

```text
none: 0
three: 6
["a", ["b", "c"]]
```

- **It is always an array**, `[]` when no argument is left for it, and a new one each call.
- **A call gives at least the parameters before it**, but those with defaults: `sum()` is an error.
- **It comes last**, after any parameter with a default, and has no default itself. Methods, constructors and arrow
  functions, in parentheses, may have one too.

### Spreading

`...values` spreads what `foreach` visits in `values` into a call's arguments, or into a literal's elements:

```javascript
function sum(...numbers) {
  var total = 0;
  foreach (var n in numbers) total += n;
  return total;
}
var some = [1, 2, 3];
writeLn(sum(...some, 4));
writeLn([0, ...some, ...#{9}]);
writeLn([..."tea"]);
var defaults = { size: "M", color: "green" };
writeLn({ ...defaults, size: "L" });
```

```text
10
[0, 1, 2, 3, 9]
["t", "e", "a"]
{"size": "L", "color": "green"}
```

- **Into a call**, each item is an argument, with others before or after it; a method's call, `new` and
  `super(…)` take them too. A function's number of arguments is then checked when it runs.
- **Into an array or a set**, each item is an element; **into a map**, a map's entries, a later key replacing an
  earlier one, as in `{ ...defaults, size: "L" }`.
- **What spreads** is what `foreach` visits: arrays, sets, strings, and instances whose class has `iterate()`; into a
  map, only maps. Spreading anything else is an error when it runs.

### Functions as values

A function is a value, which a variable may hold and a call may take. `function (…) { … }` is a function expression,
and `(…) => …` an arrow function, the same thing written shorter: its body may be an expression, which it gives, and a
single parameter needs no parentheses.

```javascript
var square = function (x) { return x * x; };
var cube = x => x * x * x;
var sum = (a, b) => a + b;
var shout = (s) => { return s + "!"; };
writeLn(square(4));
writeLn(cube(2));
writeLn(sum(1, 2));
writeLn(shout("hey"));
function apply(f, x) { return f(x); }
writeLn(apply(square, 5));
```

```text
16
8
3
hey!
25
```

An arrow function whose body is a map needs parentheses, `() => ({ a: 1 })`, since `{` would begin a block.

### Closures

A function sees the variables around it, and keeps them alive: it captures the variable itself, not a copy of its value.

```javascript
function counter() {
  var count = 0;
  return () => { count = count + 1; return count; };
}
var next = counter();
next();
next();
writeLn(next());
var fs = [];
for (var i = 0; i < 3; i = i + 1) fs.push(() => i);
foreach (var f in fs) write(f());
writeLn("");
```

```text
3
012
```

Each turn of the `for` has its own `i`, so the three closures see 0, 1 and 2. A function declared further down its
block can be called before a variable it reads has been declared; reading that variable then is an error (WL2004).

---

## 9. Collections

### Arrays

```javascript
var a = [10, 20, 30];
writeLn(a[0]);
a[1] = 25;
a.push(40);
writeLn(a);
writeLn(a.pop());
writeLn(a.length);
writeLn(a?[9]);
```

```text
10
[10, 25, 30, 40]
40
3
null
```

- `[a, b, c]` makes an array, of any values; `[]` an empty one.
- `a[i]` reads element `i`, from 0, and `a[i] = v` replaces it. The index must be an integer.
- `a.push(v)` adds at the end, `a.pop()` removes the last and gives it, and `a.length` is the number of elements.
- An index outside the array is an error:

  ```text
  coll-index-error.wl(2,9): error WL3005: The index 5 is outside 0 to 1.
  ```

### Array methods

```javascript
var a = [3, 1, 2];
writeLn([a.map(x => x * 10), a.filter(x => x > 1), a.reduce((sum, x) => sum + x, 0)]);
writeLn([a.find(x => x > 1), a.any(x => x > 2), a.all(x => x > 2), a.indexOf(2), a.contains(5)]);
var sorted = a.sort();
writeLn([sorted, a, a.reverse(), a.sort((x, y) => y - x)]);
writeLn([a.slice(1), a.concat([4]), a.join("-")]);
a.insert(0, 9);
writeLn([a.removeAt(1), a]);
```

```text
[[30, 10, 20], [3, 2], 6]
[3, true, false, 2, false]
[[1, 2, 3], [3, 1, 2], [2, 1, 3], [3, 2, 1]]
[[1, 2], [3, 1, 2, 4], "3-1-2"]
[3, [9, 1, 2]]
```

- **`sort()` and `reverse()` give a new array**, and leave the array as it was; the compiler warns where their result
  is not used, `a.sort();` alone. `insert(i, v)`, `removeAt(i)`, `push` and `pop` change the array.
- **`sort()` without a function** orders numbers by value and strings as `<` does; a mix of them is an error.
  `sort((x, y) => …)` orders by its function, which gives a negative number when `x` comes first; elements it calls
  equal keep their order.
- **The functions** `map`, `filter`, `reduce`, `find`, `any` and `all` take are given each element; an `async` one is
  awaited, and the method then gives a task, `await a.map(load)`.
- **Other names** work too: `includes` for `contains`, `select` for `map`, `where` for `filter`, `some` for `any` and
  `every` for `all`.

### Maps

```javascript
var tea = { name: "Sencha", price: 6.5 };
writeLn(tea["name"]);
tea["origin"] = "Japan";
writeLn(tea.has("origin"));
writeLn(tea.remove("price"));
writeLn(tea.length);
writeLn(tea?["missing"]);
var byId = {};
byId[7] = "seven";
writeLn(byId[7]);
```

```text
Sencha
true
true
2
null
seven
```

- `{ key: value, … }` makes a map, a key written as a name or a string being a string; `{}` is an empty map. At the start
  of a statement, `{` begins a block instead.
- `m[k]` reads a key's value, and `m[k] = v` adds or replaces it. A key's value is read only by indexing: `m.name` is a
  member of the map, not its key `"name"`.
- `m.has(k)` says whether a key is there, `m.remove(k)` removes it, giving whether it was there, and `m.length` is the
  number of entries.
- A key that is not there is an error:

  ```text
  coll-key-error.wl(2,9): error WL3007: The map has no key "b".
  ```

- Keys may be strings, integers, Booleans and `null`, compared as `==` compares them. A number, or a collection, is not
  a key.
- The order of a map's entries is not defined.

### Sets

```javascript
var s = #{"red", "green"};
writeLn(s.add("blue"));
writeLn(s.add("red"));
writeLn(s.has("green"));
writeLn(s.remove("green"));
writeLn(s.length);
```

```text
true
false
true
true
2
```

`#{a, b}` makes a set, and `#{}` an empty one. `add` gives whether the value was new, and `remove` whether it was there.
A set's values are what a map's keys may be, and their order is not defined.

### Bytes

Bytes are binary data, distinct from strings: what a file or a request holds as it is, before it is read as text. Like a string,
they are immutable.

```javascript
var b = Bytes.from("héllo");
writeLn(b);
writeLn([b.length, b[0], b[1]]);
writeLn(b.text() == "héllo");
writeLn(b.slice(0, 1).text());
writeLn(Bytes.from("café", "latin1"));
writeLn(Bytes.from([104, 105]).text());
writeLn(Bytes.from("hi") == Bytes.from([104, 105]));
writeLn(Bytes.concat([Bytes.from("ab"), 99, Bytes.from("d")]).text());
```

```text
Bytes:[104, 195, 169, 108, 108, 111]
[6, 104, 195]
true
h
Bytes:[99, 97, 102, 233]
hi
true
abcd
```

- **`Bytes.from(value, charset)`** makes bytes from a string, encoded in a charset, UTF-8 unless one is named; from an array of integers
  from 0 to 255; or from bytes, which are themselves. `new Bytes()` is an error.
- **`b.length`** is how many bytes there are, and **`b[i]`** the byte at `i`, from 0, an integer from 0 to 255. `b[i] = v` is an error.
- **`b.slice(start, end)`** gives the bytes from `start` to `end`, `end` left out meaning the end.
- **`b.text(charset)`** reads the bytes as text, UTF-8 unless a charset is named. What is not a character becomes U+FFFD.
- **The charsets**: `utf-8`, `utf-16` (or `utf-16le`), `utf-16be`, `us-ascii` and `iso-8859-1` (or `latin1`), named without regard to
  case.
- **`==` compares their contents.** Bytes are not keys of a map, nor values of a set, and `+` does not join them.
- **`Bytes.concat(parts)`** joins an array of bytes, and integers from 0 to 255, into new bytes, copying each part once.
- **`Bytes.fixedTimeEqual(a, b)`** compares two bytes in a time that does not depend on where they differ, for secrets, such as a
  signature or a token compared with the one expected, so that the time the comparison takes gives nothing away:

  ```javascript
  writeLn(Bytes.fixedTimeEqual(Bytes.from("key"), Bytes.from("key")));
  writeLn(Bytes.fixedTimeEqual(Bytes.from("key"), Bytes.from("kez")));
  ```

  ```text
  true
  false
  ```

- **A `BytesBuilder` builds bytes from many pieces**, each added once and all joined once at the end, so that building costs no more
  than the bytes built:

  ```javascript
  var builder = new BytesBuilder();
  builder.add("tea").add(32).add(Bytes.from("time"));
  writeLn(builder.length);
  writeLn(builder.toBytes().text());
  ```

  ```text
  8
  tea time
  ```

  `add` takes bytes, an integer from 0 to 255, or a string, added as UTF-8, and gives the builder, so that adds can follow one another.

### Optional indexing

`x?[k]` reads as `x[k]` does, but gives `null` where `x[k]` would find no such key or index, and when `x` is itself
`null`, so that a chain of them stops at the first `null`:

```javascript
var rows = [{ name: "Ann" }];
writeLn(rows?[0]?["name"]);
writeLn(rows?[1]?["name"]);
var none = null;
writeLn(none?[0]);
```

```text
Ann
null
null
```

A key of the wrong kind, or indexing a value that cannot be indexed, is still an error.

### Collections and equality

Arrays, maps and sets are equal only to themselves: `[1] == [1]` is false. `write` writes them as their literals.

---

## 10. Classes

```javascript
class Point {
  constructor(x, y) { this.x = x; this.y = y; }
  length() { return (this.x * this.x + this.y * this.y); }
  toString() { return $"({this.x}, {this.y})"; }
}
var p = new Point(3, 4);
writeLn(p.x);
writeLn(p.length());
writeLn(p);
var f = p.length;
writeLn(f());
writeLn(p is Point);
```

```text
3
25
(3, 4)
25
true
```

- **`new Name(…)`** makes an instance and runs the class's `constructor`. Calling a class without `new` is an error.
- **Fields** come into being when they are assigned, `this.x = x`. Reading one never assigned is an error (WL3006).
- **`this`** is the instance a method runs on; a function inside a method sees the method's `this`.
- **A method taken without a call**, `p.length`, is a function bound to its instance.
- **Every member is public.**
- **`x is Class`** is true for an instance of the class, or of a class extending it.
- **A class is in scope throughout its block**, as a function is.

### Inheritance

```javascript
class Animal {
  constructor(name) { this.name = name; }
  speak() { return this.name + " makes a sound."; }
}
class Dog extends Animal {
  constructor(name) { super(name); this.tricks = []; }
  speak() { return super.speak() + " Woof!"; }
}
var d = new Dog("Rex");
writeLn(d.speak());
writeLn(d is Animal);
writeLn(d is Dog);
writeLn(d);
```

```text
Rex makes a sound. Woof!
true
true
Dog {name: "Rex", tricks: []}
```

- A class extends at most one other. A method with a base's name replaces it, and `super.name(…)` calls the base's.
- `super(…)` in a constructor runs the base's constructor; a constructor that does not call it runs the base's first,
  without arguments. A class without a constructor passes its arguments on to its base's.
- A class without `extends` extends `Object`.
- `write` writes an instance as its class's name and its fields, unless the class gives it a `toString()`.

### Static members

```javascript
class Counter {
  static var count = 0;
  constructor() { Counter.count = Counter.count + 1; }
  static describe() { return $"{Counter.count} made"; }
}
new Counter();
new Counter();
writeLn(Counter.describe());
```

```text
2 made
```

`static` declares a member of the class itself, a method or a field, reached as `Counter.describe()`, and through an
instance too. `static const` declares one that cannot be assigned. A class's static members are inherited.

### Properties

```javascript
class Rect {
  constructor(w, h) { this.w = w; this.h = h; }
  property area { get { return this.w * this.h; } }
  property name {
    get { return field; }
    set(value) { field = value + "!"; }
  }
}
var r = new Rect(2, 3);
writeLn(r.area);
r.name = "box";
writeLn(r.name);
```

```text
6
box!
```

A property is read and assigned as a field is, but runs its `get` and `set`. `field`, inside them, is a field of the
property's own. A property with only a `get` cannot be assigned, and one with only a `set` cannot be read.

### Optional members

`x?.name` gives `null` when `x` is `null`, or has no member `name`:

```javascript
class Box { constructor(v) { this.value = v; } }
var b = null;
writeLn(b?.value);
writeLn(new Box(5)?.value);
writeLn(new Box(5)?.missing);
```

```text
null
5
null
```

### Equality and `equal`

`==` is identity for instances: two instances with the same fields are not equal. Every value has `equal(other)`,
`Object`'s giving `==`, which a class may override with a meaning of its own:

```javascript
class Money {
  constructor(cents) { this.cents = cents; }
  equal(other) { return other is Money && other.cents == this.cents; }
}
var a = new Money(100);
var b = new Money(100);
writeLn(a == b);
writeLn(a.equal(b));
```

```text
false
true
```

### `toString()`

Every value has `toString()`, the text `write` writes. A class may override it, as `Point` above does, and `write`,
`writeLn`, an interpolated string and a page's `<%= %>` then use it, wherever the instance is, inside an array too.

### Operators

A class may say what an operator does with its instances, with a static method named `operator` and the operator:

```javascript
class Vec {
  constructor(x, y) { this.x = x; this.y = y; }
  static operator +(a, b) { return new Vec(a.x + b.x, a.y + b.y); }
  toString() { return $"<{this.x}, {this.y}>"; }
}
writeLn(new Vec(1, 2) + new Vec(3, 4));
```

```text
<4, 6>
```

`+`, `-`, `*`, `/`, `<`, `<=`, `>` and `>=` may be declared, each with two parameters; `==` and `!=` may not.

### Classes of values

Every value but `null` has a class: `Boolean`, `Integer`, `Number`, `String`, `Array`, `Map`, `Set`, `Function`,
`Class`, and `Object`, which they all extend. An `extend` block gives a class more methods, everywhere, the built-in
ones included:

```javascript
extend Integer {
  twice() { return this * 2; }
}
writeLn(21.twice());
writeLn(5 is Integer);
writeLn("a" is String);
```

```text
42
true
true
```

A class may extend `Number`, `Integer`, `String` or `Boolean`, its instances then holding such a value, which arithmetic
and comparisons use; and `Array`, `Map` or `Set`, its instances then being such a collection with methods of their own:

```javascript
class Celsius extends Number {
  toFahrenheit() { return this * 9 / 5 + 32; }
}
var t = new Celsius(20);
writeLn(t);
writeLn(t.toFahrenheit());
writeLn(t + 1);
class Stack extends Array {
  top() { return this[this.length - 1]; }
}
var s = new Stack();
s.push(1);
s.push(2);
writeLn(s.top());
writeLn(s);
```

```text
Celsius:20.0
Celsius:68.0
Celsius:21.0
2
Stack:[1, 2]
```

Arithmetic keeps such a class: a result has its left operand's class, if it has one, else its right's. That is why
`toFahrenheit()` above gives a `Celsius`; a method that means another unit makes an instance of that unit's class. Such
an instance is written as its class, a colon and its value.

### Iterating an instance

`foreach` visits an instance of a class through its `iterate()` method, which gives an iterator, whose `hasNext()` and
`next()` give the items in turn:

```javascript
class Countdown {
  constructor(from) { this.from = from; }
  iterate() { return new CountdownIterator(this.from); }
}
class CountdownIterator {
  constructor(n) { this.n = n; }
  hasNext() { return this.n > 0; }
  next() { var n = this.n; this.n = n - 1; return n; }
}
foreach (var n in new Countdown(3)) write(n);
writeLn(" liftoff");
```

```text
321 liftoff
```

### Building classes while a program runs

A class can also be made, and given members, while a program runs:

```javascript
var Point = new Class("Point");
Point.setMethod("constructor", function (this, x) { this.x = x; });
Point.setMethod("describe", function (this) { return $"Point at {this.x}"; });
var p = new Point(3);
writeLn(p.describe());
writeLn(Point.__className);
writeLn(p.__instanceOf == Point);
writeLn(Point.__parent == Object);
```

```text
Point at 3
Point
true
true
```

- `new Class(name)` makes a class extending `Object`, and `new Class(name, Base)` one extending `Base`.
- A function whose first parameter is named `this` takes its receiver there.
- `setMethod`, `removeMethod`, `setProperty`, `setStaticProperty` and `setConstant` give a class its members; assigning
  a class a member it does not have, `Point.origin = …`, gives it a static one.
- `__className` is a class's name, `__instanceOf` a value's class, and `__parent` a class's base.

---

## 11. Errors and exceptions

```javascript
function divide(a, b) {
  if (b == 0) throw new Error("Cannot divide by zero.");
  return a / b;
}
try {
  writeLn(divide(1, 0));
} catch (e) {
  writeLn("Caught: " + e.message);
} finally {
  writeLn("Done.");
}
```

```text
Caught: Cannot divide by zero.
Done.
```

- **`throw value;`** throws any value; an instance of `Error`, `new Error(message)`, is usual. `Error` has `message`
  and `code`, `null` for one a program throws.
- **`try`** takes a `catch`, a `finally`, or both. `finally` runs on every way out of its `try`; a `break`, `continue`
  or `return` that would leave a `finally` is an error (WL2012).
- **`throw;`** alone, in a `catch`, throws the caught exception again:

  ```javascript
  function risky() { throw new Error("deep"); }
  try {
    try { risky(); } catch (e) { writeLn("logging: " + e.message); throw; }
  } catch (e) {
    writeLn("outer: " + e.message);
  }
  ```

  ```text
  logging: deep
  outer: deep
  ```

### Typed catches

A `catch` may name the class it catches; a `try` may have several, the first that matches catching, and a catch without
a class, last, catching anything:

```javascript
class NotFound extends Error { }
function find(key) { throw new NotFound($"No {key}."); }
try {
  find("tea");
} catch (NotFound e) {
  writeLn("Not found: " + e.message);
} catch (e) {
  writeLn("Something else.");
}
try {
  var a = [1][5];
} catch (IndexError e) {
  writeLn(e.code + ": " + e.message);
}
try {
  throw "a string";
} catch (String s) {
  writeLn("Threw " + s);
}
```

```text
Not found: No tea.
WL3005: The index 5 is outside 0 to 0.
Threw a string
```

### Runtime errors are exceptions

An error the runtime raises becomes an instance of a class extending `Error`, with its code, which a `catch` may catch:

| Class | Code | Raised by |
|---|---|---|
| `TypeError` | WL3001 | an operation on values it does not take: `"a" + 1`, a condition that is not a Boolean |
| `OverflowError` | WL3002 | an integer result that does not fit |
| `IndexError` | WL3005 | an index outside an array or a string |
| `MemberError` | WL3006 | a member a value does not have |
| `KeyError` | WL3007 | a key a map does not have |
| `InitializationError` | WL2004 | a variable read before its declaration has run |
| `CallError` | WL2005 | calling what is not a function |
| `ArgumentError` | WL2006 | a call with the wrong number of arguments |
| `TimeoutError` | WL3010 | a task that took longer than `Task.timeout` gave it |
| `IOError` | WL3011 | a file that cannot be read or written |
| `DivisionByZeroError` | WL3013 | an integer's remainder by 0 |
| `NetworkError` | WL3014 | a host that cannot be reached, or a connection that fails |

Two errors cannot be caught, and always end the program: running past the execution's limits (WL3003) and being
cancelled (WL3004).

### Uncaught exceptions

An exception nothing catches ends the program, exit code 3:

```javascript
throw new Error("Unhandled.");
```

```text
exc-uncaught.wl(1,1): error WL3008: An error was thrown and not caught: Unhandled.
```

---

## 12. Disposal

An object that holds a resource, such as a file or a connection, releases it in `dispose()`, which `using` calls when its
block ends, however it ends:

```javascript
class Resource {
  constructor(name) { this.name = name; writeLn("open " + name); }
  dispose() { writeLn("close " + this.name); }
}
using (var r = new Resource("file")) {
  writeLn("using " + r.name);
}
var r2 = new Resource("other");
r2.dispose();
r2.dispose();
```

```text
open file
using file
close file
open other
close other
```

- **An object is disposed once**: a second `dispose()` does nothing.
- **`destructor()`** is another name for `dispose()`; callers always call `dispose()`, and a class may not declare both
  (WL2011).
- **A derived class's disposal** runs, then its base's, each once, without `super.dispose()`, even when the `using`'s
  block throws:

  ```javascript
  class Base { destructor() { writeLn("base cleaned up"); } }
  class Derived extends Base { dispose() { writeLn("derived cleaned up"); } }
  using (var d = new Derived()) {
    throw new Error("failed inside");
  }
  ```

  ```text
  derived cleaned up
  base cleaned up
  dispose-derived.wl(4,3): error WL3008: An error was thrown and not caught: failed inside
  ```

- A `using` disposes one object; nest them for more. A `null` is not disposed.
- Disposal releases what an object holds; the object's memory is the collector's.
- A `dispose()` may be `async`, and `using` then awaits it.

---

## 13. Asynchrony

An `async` function gives a task at once, and runs until its first `await`; `await` waits for a task and gives its
result, or throws its exception. While a task waits, others run.

```javascript
async function fetchTea(name, ms) {
  await delay(ms);
  return name + " ready";
}
var a = fetchTea("Sencha", 20);
var b = fetchTea("Assam", 10);
writeLn(await b);
writeLn(await a);
var both = await Task.all([fetchTea("Green", 5), fetchTea("Black", 1)]);
writeLn(both);
writeLn(await Task.any([fetchTea("slow", 50), fetchTea("fast", 1)]));
```

```text
Assam ready
Sencha ready
["Green ready", "Black ready"]
fast ready
```

- **`await`** stands in an `async` function, or at a script's top level, which may always wait.
- **An `async` function starts at once**, running until its first `await`, as in C# and JavaScript:

  ```javascript
  async function work(name) {
    writeLn(name + " starts");
    await delay(1);
    writeLn(name + " ends");
  }
  var t = work("A");
  writeLn("main goes on");
  await t;
  ```

  ```text
  A starts
  main goes on
  A ends
  ```

- **`await` takes any value**: a task's result once it completes, any other value itself. `await` always lets other
  tasks run first.
- **`Task.all(values)`** completes with all their results, in order, or fails with the first exception;
  **`Task.any(values)`** completes as the first does; **`Task.timeout(task, milliseconds)`** gives the task's result, or
  fails with a `TimeoutError` when it takes longer:

  ```javascript
  async function slow() { await delay(1000); return "late"; }
  try {
    writeLn(await Task.timeout(slow(), 10));
  } catch (TimeoutError e) {
    writeLn(e.code + ": " + e.message);
  }
  ```

  ```text
  WL3010: The task did not complete within 10 milliseconds.
  ```

- **`delay(milliseconds)`** gives a task that completes after that time.
- **An exception no one awaits** still ends the program, when the program ends, so that none goes unnoticed;
  `t.forget()` says that a task's exception may go unreported:

  ```javascript
  async function background() { await delay(5); throw new Error("ignored"); }
  background().forget();
  async function failing() { throw new Error("nobody awaited me"); }
  failing();
  writeLn("main ends");
  ```

  ```text
  main ends
  async-forget.wl(3,28): error WL3008: An error was thrown and not caught: nobody awaited me
  ```

- **One at a time.** A program's tasks run on one thread, taking turns only at `await`, so that no two run at once and
  nothing needs a lock.
- **When the program's top level ends**, the program ends, and its tasks still running are cancelled.

---

## 14. Modules

A module is a `.wl` file whose declarations marked `export` other files import:

```javascript
// lib/shapes.wl
export class Square {
  constructor(side) { this.side = side; }
}
export function area(square) { return square.side * square.side; }
export const unit = "cm";
```

```javascript
import shapes from "lib/shapes.wl";
import { area, Square } from "lib/shapes.wl";
writeLn(area(new Square(3)));
writeLn(shapes.unit);
```

```text
9
cm
```

- **`import name from "path";`** gives the module itself, whose exports are its members; **`import { a, b } from
  "path";`** gives its exports by name. Both may be used together.
- **A path** is from the importing file's folder; `.wl` may be left out.
- **Imports stand at a file's top level**, not inside a block or a function, and the modules run before the rest of
  the file, in their order.
- **A module runs once** per execution, the first time it is imported; every later import gets the same module.
- **Only exports are seen**: a function, class, `var` or `const` declared `export`. An importer cannot assign a
  module's variable; the module changes its own.
- **A module may import itself**, through others too: the second import gets the module as it is so far.
- **`await Module.import(path)`** imports a module while the program runs, its path any string:

  ```javascript
  var shapes = await Module.import("lib/shapes.wl");
  writeLn(shapes.unit);
  ```

  ```text
  cm
  ```

### Module parameters

A module may declare parameters, as a function does, at its top before anything else, `param name;` or with a default,
`param size = 1;`. An import gives them in a map, and each set of values makes an instance of the module of its own:

```javascript
// lib/sized.wl
param size = 1;
export function describe() { return $"size {size}"; }
```

```javascript
import small from "lib/sized.wl" { size: 2 };
import big from "lib/sized.wl" { size: 10 };
writeLn(small.describe());
writeLn(big.describe());
```

```text
size 2
size 10
```

A value for a parameter the module does not declare, or a missing one without a default, is an error (WL2016).

### Script parameters

A script's own parameters are given on the command line, each a string, which the script converts where it needs another
kind:

```javascript
param name = "World";
param times = "1";
var n = Integer.parse(times);
for (var i = 0; i < n; i = i + 1) writeLn($"Hello, {name}!");
```

`weblingo run script-params.wl name=Ann times=2` writes:

```text
Hello, Ann!
Hello, Ann!
```

---

## 15. The standard library

The standard library is a set of modules named `std:` and a name, imported as any module is. The WebLingo Standard
Library Reference (`Documents/weblingo-standard-library-reference.md`) lists them, and every built-in class's members.

### `std:system`

Every file imports it implicitly, so its functions are always in scope:

| Function | What it does |
|---|---|
| `write(value)` | writes a value's text, without a line break |
| `writeLn(value)` | writes a value's text and a line break |
| `delay(milliseconds)` | a task that completes after that time |

`write` writes where its host says: the console under `weblingo`, a response's body in a page.

### `std:tasks`

`all`, `any` and `timeout`, the same as `Task.all`, `Task.any` and `Task.timeout`: `import { all } from "std:tasks";`.

### `std:fs`

Files and folders:

```javascript
import fs from "std:fs";
await fs.writeText("note.txt", "A cup of tea.");
writeLn(await fs.readText("note.txt"));
writeLn(fs.existsSync("note.txt"));
await fs.remove("note.txt");
writeLn(fs.existsSync("note.txt"));
```

```text
A cup of tea.
true
false
```

| Function | What it does |
|---|---|
| `readText(path)` | a file's text, UTF-8 unless a second argument names another encoding |
| `writeText(path, text)` | writes a file, replacing it |
| `appendText(path, text)` | adds to the end of a file |
| `exists(path)` | whether a file or a folder is there |
| `remove(path)` | deletes a file |
| `list(path)` | the names in a folder |
| `makeFolder(path)`, `removeFolder(path)` | makes and removes a folder |
| `File.open(path, mode)` | a file opened as a handle, with `read`, `write` and `dispose()` |

Each is asynchronous, giving a task, and each has a synchronous form whose name ends in `Sync`: `readTextSync(path)`.
A relative path is from the program's current folder. A file that cannot be read or written is an `IOError`, WL3011,
naming the path as the script gave it.

### `std:http` and `std:net`

Other servers, reached by HTTP, `std:http`'s `get(url, headers)`, `post(url, body, headers)` and `request(method, url, body, headers)`, each a task of the response, a map of its `"status"`, `"headers"`, `"body"` and `"bytes"`, or by TCP, `std:net`'s `Socket.connect(host, port)`, with `read()`, `write(data)` and `dispose()`. Given a server on this machine, at port 8080, that has a file `menu.txt`:

```javascript
import http from "std:http";
var response = await http.get("http://localhost:8080/menu.txt");
writeLn(response["status"]);
write(response["body"]);
```

```text
200
Sencha
Genmaicha
```

A script `weblingo` runs may reach any host, and a site's only those its `wasp.config` allows. A host that cannot be reached, or a connection that fails, is a `NetworkError`, WL3014. The Standard Library Reference describes both modules.

### Native modules

A native module is code compiled for .NET, an assembly that extends what scripts can use, as Python's C extensions do.
It is imported by a scheme, a name the host is configured with for a folder of native modules,
`import db from "native:sqlite";`, which finds `sqlite.dll` in that folder. A script's `weblingo.config` names the
folders, `{ "schemes": { "native": "C:/WebLingo/native" } }`, and the command line's `--scheme native=folder` comes
before it. A native module may export functions and classes, used as any others are. The standard modules are native
modules themselves, in the `std` folder beside `weblingo`.

---

## 16. Diagnostic codes

Every error has a code, `WL` and four digits, whose first digit says when it is found:

| Code | Meaning |
|---|---|
| **WL1xxx** | **the text does not parse** |
| WL1001 | a syntax error: something where it cannot stand |
| WL1002 | a missing `;` |
| WL1003 | a character that cannot begin anything |
| WL1004 | a malformed number, or one too large |
| WL1005 | a `/*` comment without its `*/` |
| WL1006 | a declaration without a value: `var x;` |
| WL1007 | the left of `=` is not a variable |
| WL1008 | code nested too deeply |
| WL1009 | an invalid escape in a string |
| WL1010 | a string not closed on its line |
| WL1011 | a multi-line string without its closing quotes |
| WL1012 | a multi-line or interpolated string written wrongly |
| **WL2xxx** | **the code does not make sense** |
| WL2001 | a name that is not defined |
| WL2002 | a name declared twice in one scope |
| WL2003 | assigning a constant |
| WL2004 | a variable read before its declaration has run |
| WL2005 | calling what is not a function, or a class without `new` |
| WL2006 | a call with the wrong number of arguments |
| WL2007 | `break` or `continue` outside a loop |
| WL2008 | `return` outside a function |
| WL2009 | `this` outside a method |
| WL2010 | `throw;` outside a `catch` |
| WL2011 | a class declaring both `dispose()` and `destructor()` |
| WL2012 | `break`, `continue` or `return` leaving a `finally` |
| WL2013 | a class extending itself |
| WL2014 | `await` outside an `async` function |
| WL2015 | a module that cannot be found |
| WL2016 | a module's or a script's parameter missing, or not declared |
| WL2017 | a warning: the new array `sort()` or `reverse()` gives, discarded |
| **WL3xxx** | **the program fails as it runs** |
| WL3001 | an operation on values it does not take |
| WL3002 | an integer result that does not fit in 64 bits |
| WL3003 | the execution's limits exceeded: instructions, memory or call depth |
| WL3004 | the execution cancelled by its host |
| WL3005 | an index outside an array or a string |
| WL3006 | a member a value does not have |
| WL3007 | a key a map does not have |
| WL3008 | an exception nothing caught |
| WL3009 | waiting for a task nothing will complete |
| WL3010 | a task that took longer than `Task.timeout` gave it |
| WL3011 | a file that cannot be read or written |
| WL3013 | an integer's remainder by 0 |
| WL3014 | a host that cannot be reached, or a connection that fails |
| **WL4xxx** | **bytecode** |
| WL4001 | bytecode that fails verification, such as a damaged `.wlbc` |
| WL4002 | a program too large for the bytecode |
| **WL5xxx** | **templates (WASP)** |
| WL5001 | a `<%` without its `%>` |
| WL5002 | a template that cannot be found, or is outside the site |
| WL5003 | an include that includes itself |
| WL5004 | includes nested too deeply, or too large |
| WL5005 | a component that cannot be found or compiled |
| WL5006 | a component's or a layout's value not declared, or missing |
| WL5007 | a layout statement where a page cannot have one |

---

## 17. What is not in the language yet

WebLingo is pre-alpha, and some of what other languages have is still to come. These are not in the language yet:

- **Library**: database access.
