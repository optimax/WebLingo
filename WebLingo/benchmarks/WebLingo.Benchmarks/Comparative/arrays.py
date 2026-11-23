a = []
for i in range(1000000):
    a.append(i % 1000)
total = 0
for round in range(3):
    for i in range(len(a)):
        total += a[i]
        a[i] = (a[i] * 7 + round) % 1000
print(total)
