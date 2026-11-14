// Asynchronous suspension: tasks suspended at await, each resumed in turn by the scheduler.
async function work(n) {
    var total = 0;
    for (var i = 0; i < n; i = i + 1) {
        total = total + await i;
    }
    return total;
}
var tasks = [];
for (var t = 0; t < 100; t = t + 1) {
    tasks.push(work(1000));
}
var sum = 0;
foreach (var task in tasks) {
    sum = sum + await task;
}
writeLn(sum);
