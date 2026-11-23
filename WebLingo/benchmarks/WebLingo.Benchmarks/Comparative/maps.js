const m = new Map();
for (let i = 0; i < 200000; i++) { m.set("k" + String(i % 50000), i); }
let hits = 0;
for (let i = 0; i < 200000; i++) {
  const k = "k" + String(i);
  if (m.has(k)) { hits += m.get(k); }
}
console.log(hits);
