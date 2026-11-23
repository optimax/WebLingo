m = {}
for i in range(200000):
    m["k" + str(i % 50000)] = i
hits = 0
for i in range(200000):
    k = "k" + str(i)
    if k in m:
        hits += m[k]
print(hits)
