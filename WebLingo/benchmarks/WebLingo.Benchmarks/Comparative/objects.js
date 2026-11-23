class Point {
  constructor(x, y) { this.x = x; this.y = y; }
  add(other) { return new Point(this.x + other.x, this.y + other.y); }
  sum() { return this.x + this.y; }
}
let p = new Point(0, 0);
const step = new Point(1, 2);
let total = 0;
for (let i = 0; i < 1000000; i++) {
  p = p.add(step);
  total += p.sum() % 7;
}
console.log(total);
