local sum = 0
for i = 0, 2999 do
  for j = 0, 2999 do
    if (i + j) % 3 == 0 then sum = (sum + i * j) % 1000003 else sum = sum + 1 end
  end
end
print(sum)
