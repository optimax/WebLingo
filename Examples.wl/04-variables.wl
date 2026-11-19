// 04-variables.wl: var and const, block scope, and closures.
//
// var declares a variable and const a constant, both in scope from their declaration to the end of their block, as
// JavaScript's let and const are; let itself is reserved and declares nothing. A declaration always has a value, so
// that no variable is ever uninitialized, and no assignment creates a global by accident (see does-not-compile/).

var count = 1;
const limit = 3;
{
  var count = 10;                 // a block may shadow an outer name
  writeLn(["inner", count]);
}
writeLn(["outer", count, limit]);

// const freezes the binding, not the value: the array itself may still change.
const teas = ["green"];
teas.push("black");
writeLn(teas);

// Each turn of a loop has its own binding, which a closure keeps.
var later = [];
for (var i = 0; i < limit; i++) {
  later.push(() => i * 10);
}
writeLn(later.map(f => f()));

// A closure keeps the variable itself, not a copy of its value.
function counter() {
  var n = 0;
  return () => { n += 1; return n; };
}
var next = counter();
next();
next();
writeLn(next());

// Functions are hoisted in their block, so they may call each other in any order.
writeLn(isEven(10));
function isEven(n) { return if (n == 0) true else isOdd(n - 1); }
function isOdd(n) { return if (n == 0) false else isEven(n - 1); }

/* Output:
["inner", 10]
["outer", 1, 3]
["green", "black"]
[0, 10, 20]
3
true
*/
