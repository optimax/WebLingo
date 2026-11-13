// Nested for loops: int64 arithmetic, comparisons and if, a million times.
var sum = 0;
for (var i = 0; i < 1000; i = i + 1) {
    for (var j = 0; j < 1000; j = j + 1) {
        if (j < i) sum = sum + j; else sum = sum - 1;
    }
}
writeLn(sum);
