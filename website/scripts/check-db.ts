import { PrismaClient } from "@prisma/client";

const prisma = new PrismaClient();

async function main() {
  try {
    const counts = {
      customers: await prisma.customer.count(),
      licenses: await prisma.license.count(),
      licenseTokens: await prisma.licenseToken.count(),
      activations: await prisma.activation.count()
    };

    console.log(JSON.stringify(counts, null, 2));
  } finally {
    await prisma.$disconnect();
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
