// Arrays and maps: elements and entries read, written and visited with foreach.
var words = ["alpha", "beta", "gamma", "delta"];
var counts = {};
foreach (var w in words) counts[w] = 0;
for (var round = 0; round < 5000; round = round + 1) {
    foreach (var w in words) counts[w] = counts[w] + w.length;
}
var total = 0;
foreach (var (w, c) in counts) total = total + c;
var numbers = [];
for (var i = 0; i < 20000; i = i + 1) numbers.push(i);
var sum = 0;
foreach (var n in numbers) sum = sum + n;
for (var i = 0; i < numbers.length; i = i + 1) numbers[i] = numbers[i] * 2;
writeLn(total + sum + numbers[19999]);
