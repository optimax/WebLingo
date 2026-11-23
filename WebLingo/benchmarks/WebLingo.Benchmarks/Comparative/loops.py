total = 0
for i in range(3000):
    for j in range(3000):
        if (i + j) % 3 == 0:
            total = (total + i * j) % 1000003
        else:
            total = total + 1
print(total)
