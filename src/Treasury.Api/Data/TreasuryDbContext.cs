using Microsoft.EntityFrameworkCore;

namespace Treasury.Api.Data;

// Registered with DbContext pooling (Aspire AddNpgsqlDbContext): keep a single public
// constructor taking DbContextOptions and don't inject scoped services.
public class TreasuryDbContext(DbContextOptions<TreasuryDbContext> options) : DbContext(options);
