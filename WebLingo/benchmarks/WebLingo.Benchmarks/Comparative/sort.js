let x = 42;
const a = [];
for (let i = 0; i < 200000; i++) { x = (x * 16807) % 2147483647; a.push(x); }
const sorted = a.sort((p, q) => p - q);
let check = 0;
for (let i = 0; i < sorted.length; i += 1000) { check = (check + sorted[i]) % 1000000007; }
console.log(check);
