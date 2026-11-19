// 01-hello.wl: a first script. Run it from this folder with: weblingo run 01-hello.wl
//
// A script is a .wl file, which weblingo compiles and runs. Every statement ends with a semicolon: WebLingo never
// inserts one for you, so a line break never changes what a program means.

writeLn("Hello, WebLingo!");

// write and writeLn are std:system's, which every script has without an import. Importing them is allowed, and means
// the same: import { write, writeLn } from "std:system";
write("write leaves the line open, ");
writeLn("writeLn ends it.");

/* A block comment
   may span lines. */
var answer = 42; // a comment after a statement
writeLn(answer);
writeLn([1, "two", 3.0, null, true]); // any value can be written, as it would be typed

/* Output:
Hello, WebLingo!
write leaves the line open, writeLn ends it.
42
[1, "two", 3.0, null, true]
*/
