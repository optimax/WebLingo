const a = [];
for (let i = 0; i < 1000000; i++) { a.push(i % 1000); }
let sum = 0;
for (let round = 0; round < 3; round++) {
  for (let i = 0; i < a.length; i++) {
    sum += a[i];
    a[i] = (a[i] * 7 + round) % 1000;
  }
}
console.log(sum);
