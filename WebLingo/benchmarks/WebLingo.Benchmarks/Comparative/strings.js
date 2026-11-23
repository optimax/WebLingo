let total = 0;
for (let i = 0; i < 300000; i++) {
  const s = "item" + String(i);
  if (s < "item5") { total += s.length; } else { total += 1; }
  if (s.includes("99")) { total += 2; }
}
console.log(total);
