total = 0
for i in range(300000):
    s = "item" + str(i)
    if s < "item5":
        total += len(s)
    else:
        total += 1
    if "99" in s:
        total += 2
print(total)
