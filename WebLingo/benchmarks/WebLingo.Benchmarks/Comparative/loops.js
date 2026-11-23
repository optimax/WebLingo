let sum = 0;
for (let i = 0; i < 3000; i++) {
  for (let j = 0; j < 3000; j++) {
    if ((i + j) % 3 === 0) { sum = (sum + i * j) % 1000003; } else { sum = sum + 1; }
  }
}
console.log(sum);
