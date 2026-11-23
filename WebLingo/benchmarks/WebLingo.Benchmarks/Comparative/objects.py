class Point:
    def __init__(self, x, y):
        self.x = x
        self.y = y
    def add(self, other):
        return Point(self.x + other.x, self.y + other.y)
    def sum(self):
        return self.x + self.y
p = Point(0, 0)
step = Point(1, 2)
total = 0
for i in range(1000000):
    p = p.add(step)
    total += p.sum() % 7
print(total)
