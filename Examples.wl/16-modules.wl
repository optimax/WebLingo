// 16-modules.wl: importing modules, statically and dynamically.
//
// A module is a .wl file whose exports are its instance's members. Its path is from the importing file's folder.
// A static import is compiled with the program; Module.import compiles one while the program runs.

import teas from "lib/teas.wl";                 // the module's instance
import { Tea, serve } from "lib/teas.wl";       // and names from it: the same module, run once

var sencha = new Tea("Sencha", 4.5);
writeLn([serve(sencha), teas.serve("tea"), teas.served]);

// An export is the module's to change: an importer reads it, and cannot assign it. (A message names a module by its
// path from the current folder, which the slices leave out.)
try {
  teas.served = 0;
} catch (TypeError e) {
  writeLn(e.message.slice(e.message.indexOf("cannot")));
}

// What a module does not export cannot be reached.
try {
  writeLn(teas.secret);
} catch (MemberError e) {
  writeLn(e.message.slice(e.message.indexOf("has no")));
}

// A dynamic import, its path and its values known only when it runs; other values give the module a new instance.
var dollars = await Module.import("lib/teas.wl", { currency: "CHF " });
writeLn([dollars.served, new dollars.Tea("Assam", 3.0).toString()]);

// A module a script defines in code, rather than in a file: a class extending Module.
class Counter extends Module {
  constructor() { super("counter"); this.count = 0; }
  next() { this.count += 1; return this.count; }
}
var counter = new Counter();
counter.next();
writeLn([counter.next(), counter is Module]);

/* Output:
teas.wl runs, its prices in $
["Serving Sencha at $4.50", "Serving tea", 2]
cannot be assigned: an export is its module's to change.
has no member 'secret'.
teas.wl runs, its prices in CHF
[0, "Assam at CHF 3.00"]
[2, true]
*/
