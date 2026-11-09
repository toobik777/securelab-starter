function later(value, ms) {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms));
}

async function main() {
  console.log("1: before");
  const promise = later("done", 100);
  console.log("2:", promise instanceof Promise);
  const value = await promise;
  console.log("3:", value);
}

main();
console.log("4: main returned");
