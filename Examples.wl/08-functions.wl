// 08-functions.wl: functions, as declarations, expressions and arrows; defaults, rest parameters, and receivers.

function greet(name, greeting = "Hello") {
  return $"{greeting}, {name}!";
}
writeLn([greet("Ann"), greet("Bo", "Hi")]);

// A rest parameter gathers the arguments after the others into an array, [] when there are none.
function log(level, ...parts) { return $"[{level}] " + " ".join(parts); }
writeLn([log("info", "server", "started"), log("warn")]);

// Functions are values: passed, returned and stored.
var twice = f => x => f(f(x));
var addThree = x => x + 3;
writeLn(twice(addThree)(10));
var operations = { add: (a, b) => a + b, times: function (a, b) { return a * b; } };
writeLn([operations["add"](2, 3), operations["times"](2, 3)]);

// A function whose first parameter is named this takes its receiver: called as a method, or given it first.
function describe(this, unit) { return $"{this.length} {unit}"; }
writeLn(describe("tea", "letters"));

// Every call is checked: a function called with too few or too many arguments is an error, never undefined.
function pair(a, b) { return [a, b]; }
try {
  var f = pair;
  f(1);
} catch (ArgumentError e) {
  writeLn(e.message);
}

/* Output:
["Hello, Ann!", "Hi, Bo!"]
["[info] server started", "[warn] "]
16
[5, 6]
3 letters
'pair' takes 2 arguments, not 1.
*/
