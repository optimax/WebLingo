# Getting Started with WebLingo

In this course you’ll write small WebLingo scripts and put them together into a command-line tea menu. Each lesson
gives you a file to save, an output to compare, and something to try on your own.

## 1. Run your first script

First install WebLingo, as the [README](../README.md#try-it) says: on Windows from its zip, on Linux from its archive
or its Debian package. In a new console, `weblingo --version` then shows the version you installed.

Create a folder for the lessons. Save this as `hello.wlg` in that folder. WebLingo source files currently use `.wlg`.

```javascript
import { writeLn } from "std:console";

writeLn("Hello, WebLingo!");
```

Its first line imports `writeLn` from `std:console`, the module of the console's writing and reading: a script imports
what it uses, and a script that writes to its console imports `std:console`. From your lesson folder, run
`weblingo run hello.wlg`. It prints:

```text
Hello, WebLingo!
```

If you’re working from a checkout and haven’t installed WebLingo, install the .NET 10 SDK and run the following
from the repository root. Replace `PATH_TO_HELLO.wlg` with the path to your file.

```sh
dotnet run --project WebLingo/src/WebLingo.Cli -- run "PATH_TO_HELLO.wlg"
```

Use the same command with each lesson’s filename. To check a script without running it, replace `run` with `check`.
See the [build instructions](../WebLingo/README.md) for more ways to run the CLI.

**Try it.** Change the greeting. Then remove the semicolon after `writeLn` and use `check` to see where the parser
reports the error. Put the semicolon back before continuing.

## 2. Store values and format a message

Save as `order.wlg`. Use `const` for a name you won’t assign again and `var` for one you will. Both need an initial
value. A string starting with `$"` can insert the result of an expression inside braces.

```javascript
import { writeLn } from "std:console";

const tea = "sencha";
const centsPerCup = 250;
var cups = 2;
cups += 1;
writeLn($"{cups} cups of {tea} cost {cups * centsPerCup} cents.");
```

```text
3 cups of sencha cost 750 cents.
```

**Try it.** Start with four cups. The total should be 1,250 cents. Keep the price in integer cents so you can
calculate a total without floating-point rounding.

## 3. Make decisions and repeat work

Save as `steeping.wlg`. A condition takes a Boolean, so test `minutes > 3` explicitly. A `foreach` loop visits
each item in the array in order. The name `minutes` belongs to the loop.

```javascript
import { writeLn } from "std:console";

var times = [2, 4, 3];
foreach (var minutes in times) {
    if (minutes > 3) {
        writeLn($"{minutes} min: strong");
    } else {
        writeLn($"{minutes} min: mild");
    }
}
```

```text
2 min: mild
4 min: strong
3 min: mild
```

**Try it.** Add a five-minute steep to the array. Then use `>=` so three minutes also counts as strong.

## 4. Look up information in a map

Save as `prices.wlg`. A map stores values by key. Use `prices["sencha"]` when the key must exist. Optional
indexing, `prices?[name]`, gives `null` for a missing key. The `??` operator gives a default when its left
side is `null`.

```javascript
import { writeLn } from "std:console";

var prices = { sencha: 250, assam: 300 };
writeLn(prices["sencha"]);
writeLn(prices?["herbal"] ?? "unlisted");
```

```text
250
unlisted
```

**Try it.** Add `herbal: 0` to the map. The second line prints `0` because zero is a price and `null` represents
the missing value.

## 5. Give repeated work a name

Save as `receipts.wlg`. A function can take arguments and return a value. Give a parameter a default to let callers
omit it.

```javascript
import { writeLn } from "std:console";

function receipt(tea, centsPerCup, cups = 1) {
    return $"{tea} x{cups}: {centsPerCup * cups} cents";
}

writeLn(receipt("sencha", 250));
writeLn(receipt("assam", 300, 2));
```

```text
sencha x1: 250 cents
assam x2: 600 cents
```

**Try it.** Return the total from a second function, then call that function from `receipt`.

## 6. Give your script parameters and handle bad input

Save as `cups.wlg`. Declare parameters at the top of the file. The command line supplies them as text.
Use `Integer.parse` to get an integer before doing arithmetic. Catch `TypeError` to handle text that
cannot be parsed as an integer.

```javascript
param cups = "2";
import { writeLn } from "std:console";

try {
    var count = Integer.parse(cups);
    if (count > 0) {
        writeLn($"Total: {count * 250} cents");
    } else {
        writeLn("Enter a positive number of cups.");
    }
} catch (TypeError error) {
    writeLn("Enter a whole number of cups.");
}
```

Run `weblingo run cups.wlg cups=3`. Then run `weblingo run cups.wlg cups=three`.

```text
Total: 750 cents
Enter a whole number of cups.
```

**Try it.** Try `cups=0` and run the script without arguments to see the default in use.

## 7. Put related values and behavior in a class

Save as `tea.wlg`. The constructor sets the fields of a new object. Its methods use `this` to access those fields.

```javascript
import { writeLn } from "std:console";

class Tea {
    constructor(name, minutes) {
        this.name = name;
        this.minutes = minutes;
    }

    instructions() {
        return $"Steep {this.name} for {this.minutes} minutes.";
    }
}

var tea = new Tea("sencha", 2);
writeLn(tea.instructions());
```

```text
Steep sencha for 2 minutes.
```

**Try it.** Create an array of two `Tea` objects and print their instructions with `foreach`.

## 8. Wait for a task

Save as `timer.wlg`. An `async` function gives the caller a task. Use `await` to get its result.
You can use `await` at the top level of a script as well as inside an `async` function.

```javascript
import { writeLn } from "std:console"; import { delay } from "std:system";

async function brew(name) {
    await delay(100);
    return $"{name} is ready.";
}

writeLn("Brewing...");
writeLn(await brew("sencha"));
```

```text
Brewing...
sencha is ready.
```

**Try it.** Start two brews with `var first = brew("sencha");` and `var second = brew("assam");` before
awaiting either result. Print both results in the order you want to serve them.

## 9. Build a two-file tea menu

A module is a script that exposes declarations with `export`. Save this one as `menu-data.wlg`.

```javascript
const prices = { sencha: 250, assam: 300, oolong: 275 };

export function priceFor(tea) {
    return prices?[tea];
}
```

Save the following as `menu.wlg` in the same folder. It imports `priceFor` by name. The path in the
import is relative to `menu.wlg`. Run the main script, which loads the module.

```javascript
param tea = "sencha";
import { writeLn } from "std:console";
import { priceFor } from "./menu-data.wlg";

var cents = priceFor(tea);
if (cents == null) {
    writeLn($"No price for {tea}.");
} else {
    writeLn($"{tea}: {cents} cents per cup");
}
```

Run `weblingo run menu.wlg tea=assam` and then `weblingo run menu.wlg tea=mint`.

```text
assam: 300 cents per cup
No price for mint.
```

**Try it.** Give `menu.wlg` a `cups` parameter. Reuse the parsing and validation from lesson 6 to print
a total when the tea exists and the number of cups is positive.

## Keep going

- Browse the [example scripts](../Examples.wlg/) for strings, collections, classes, modules, and more. Many include
  their expected output at the end of the file.
- Use the [language reference](weblingo-language-reference.md) for syntax and the
  [standard library reference](weblingo-standard-library-reference.md) for built-in classes and modules.
- Try the [WASP user manual](wasp-user-manual.md) when you’re ready to put WebLingo into a web page.
