// Closures made and called: each shares a captured cell with the function that made it.
function counter() {
    var count = 0;
    return function (by) {
        count = count + by;
        return count;
    };
}
var total = 0;
for (var i = 0; i < 20000; i = i + 1) {
    var next = counter();
    for (var j = 0; j < 10; j = j + 1) total = total + next(j);
}
writeLn(total);
