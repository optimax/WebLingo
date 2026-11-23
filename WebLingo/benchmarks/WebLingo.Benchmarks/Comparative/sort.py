from functools import cmp_to_key
x = 42
a = []
for i in range(200000):
    x = (x * 16807) % 2147483647
    a.append(x)
ordered = sorted(a, key=cmp_to_key(lambda p, q: p - q))
check = 0
for i in range(0, len(ordered), 1000):
    check = (check + ordered[i]) % 1000000007
print(check)
