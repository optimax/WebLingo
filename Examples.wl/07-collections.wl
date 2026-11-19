// 07-collections.wl: arrays, maps and sets.

// Arrays: indexed from 0, of any values, mixed.
var teas = ["sencha", "assam", "oolong"];
teas.push("rooibos");
writeLn([teas.length, teas[0], teas.pop(), teas]);
writeLn([teas.indexOf("assam"), teas.contains("darjeeling"), teas.slice(1), teas.concat(["chai"])]);

// sort and reverse give a new array, leaving the array as it was; the compiler warns where their result is dropped.
var sorted = teas.sort();
writeLn([sorted, teas, teas.reverse()]);
writeLn([3, 1, 2].sort((a, b) => b - a));

// Functions over arrays, under their usual names, and JavaScript's and C#'s too (filter or where, map or select).
var prices = [4.5, 12.0, 7.25, 30.0];
writeLn(prices.filter(p => p < 10).map(p => p * 2));
var sum = prices.reduce((total, p) => total + p, 0.0);
writeLn([sum, prices.find(p => p > 10), prices.any(p => p > 25), prices.all(p => p > 0)]);
writeLn(prices.where(p => p > 10).select(p => p / 2));

// Maps: a key written as a name is a string. Keys are read by indexing only, never as members.
var stock = { sencha: 12, assam: 0 };
stock["oolong"] = 5;
writeLn([stock["sencha"], stock.has("assam"), stock.remove("assam"), stock.length]);
foreach (var (tea, count) in stock) {
  if (count > 6) writeLn($"{tea}: plenty");
}
writeLn(stock?["darjeeling"] ?? 0);   // ?[ gives null for a missing key, where [ is an error

// Sets: each value once.
var seen = #{"sencha", "assam"};
writeLn([seen.add("sencha"), seen.add("chai"), seen.has("chai"), seen.length]);

// Spreading: into calls, and into array, set and map literals.
var defaults = { size: "M", milk: false };
var order = { ...defaults, milk: true };
writeLn([[...teas, "chai"], #{...[1, 1, 2]}.length, order["size"], order["milk"]]);
function total(...amounts) { return amounts.reduce((a, b) => a + b, 0); }
writeLn(total(...[1, 2, 3], 4));

// foreach visits an array's elements, a set's values and a string's characters.
var letters = [];
foreach (var c in "tea") letters.push(c.toUpper());
writeLn(letters);

/* Output:
[4, "sencha", "rooibos", ["sencha", "assam", "oolong"]]
[1, false, ["assam", "oolong"], ["sencha", "assam", "oolong", "chai"]]
[["assam", "oolong", "sencha"], ["sencha", "assam", "oolong"], ["oolong", "assam", "sencha"]]
[3, 2, 1]
[9.0, 14.5]
[53.75, 12.0, true, true]
[6.0, 15.0]
[12, true, true, 2]
sencha: plenty
0
[false, true, true, 3]
[["sencha", "assam", "oolong", "chai"], 2, "M", true]
10
["T", "E", "A"]
*/
