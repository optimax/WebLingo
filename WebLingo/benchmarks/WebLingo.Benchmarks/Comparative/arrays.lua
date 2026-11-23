local a = {}
for i = 0, 999999 do a[#a + 1] = i % 1000 end
local sum = 0
for round = 0, 2 do
  for i = 1, #a do
    sum = sum + a[i]
    a[i] = (a[i] * 7 + round) % 1000
  end
end
print(sum)
