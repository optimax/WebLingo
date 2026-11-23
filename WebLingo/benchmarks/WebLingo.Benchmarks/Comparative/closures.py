def counter(start):
    n = start
    def next_value():
        nonlocal n
        n += 1
        return n
    return next_value
total = 0
for i in range(2000):
    next_value = counter(i)
    for j in range(1000):
        total += next_value()
print(total)
