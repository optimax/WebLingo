function counter(start) {
  let n = start;
  return () => { n += 1; return n; };
}
let total = 0;
for (let i = 0; i < 2000; i++) {
  const next = counter(i);
  for (let j = 0; j < 1000; j++) { total += next(); }
}
console.log(total);
