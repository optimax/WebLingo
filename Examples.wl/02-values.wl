// 02-values.wl: the kinds of values, and how numbers behave.
//
// Integers are 64 bits and never silently overflow; numbers are IEEE 754 binary64; decimals, written with an m, are
// exact, for money. Every value but null has a class, which `is` tests.

import math from "std:math";

writeLn([42, 0x2A, 0b101010, 1_000_000]);          // the same integer three ways, and digits grouped
writeLn([7 / 2, 7 \ 2, 7 % 2, -7 % 3]);            // / always divides exactly, \ two integers; % keeps the dividend's sign
writeLn([0.1 + 0.2, 0.1m + 0.2m]);                  // binary64 rounds; a decimal does not
writeLn([1.50m, 1.5m + 1, 19.99m * 3]);             // a decimal written with its fewest digits; an integer joins it exactly
writeLn([1 == 1.0, NaN == NaN, Infinity > 1e308]);  // integers and numbers compare as numbers, NaN as IEEE 754 says

writeLn([5 is Integer, 5.0 is Number, 5m is Decimal, "5" is String, true is Boolean, null is Object]);
writeLn([[1] is Array, {} is Map, #{} is Set, writeLn is Function, Integer is Class]);

// Converting is always explicit: a string is read as a number only when the program asks, and a number becomes an
// integer by std:math's floor, ceiling, round or truncate.
writeLn([Integer.parse("10") + 1, Number.parse("2.5") * 2, Boolean.parse("true")]);
writeLn("total: " + (40 + 2).toString());
writeLn([math.floor(7 / 2), math.round(2.5), math.round(-2.5), math.sqrt(2), math.max(3, 9.5, 4), math.PI]);

// There is null, and no undefined: a variable always has a value, and a missing member is an error, not undefined.
var nothing = null;
writeLn([nothing, nothing == null, nothing ?? "a default"]);

/* Output:
[42, 42, 42, 1000000]
[3.5, 3, 1, -1]
[0.30000000000000004, 0.3m]
[1.5m, 2.5m, 59.97m]
[true, false, true]
[true, true, true, true, true, false]
[true, true, true, true, true]
[11, 5.0, true]
total: 42
[3, 3, -3, 1.4142135623730951, 9.5, 3.141592653589793]
[null, true, "a default"]
*/
