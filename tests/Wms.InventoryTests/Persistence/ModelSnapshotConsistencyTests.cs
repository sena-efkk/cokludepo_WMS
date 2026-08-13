using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class ModelSnapshotConsistencyTests
{
    [Fact]
    public void Model_has_no_pending_changes()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail())
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new InventoryDbContext(options);
        var current = db.GetService<IDesignTimeModel>().Model
            .ToDebugString(MetadataDebugStringOptions.LongDefault);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "model_current.txt"), current);

        var snapshot = new InventoryDbContextModelSnapshot();
        var method = typeof(InventoryDbContextModelSnapshot)
            .GetMethod("BuildModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var builder = new ModelBuilder();
        method.Invoke(snapshot, [builder]);

        var runtimeInitializer = db.GetService<IModelRuntimeInitializer>();
        var snapshotModel = runtimeInitializer.Initialize(builder.FinalizeModel(), designTime: true, validationLogger: null);

        var differ = db.GetService<IMigrationsModelDiffer>();
        var snapshotRelational = snapshotModel.GetRelationalModel();
        var currentRelational = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = differ.GetDifferences(snapshotRelational, currentRelational);

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "differ_ops.txt"),
            string.Join(Environment.NewLine, operations.Select(o =>
                o is Microsoft.EntityFrameworkCore.Migrations.Operations.AlterColumnOperation alter
                    ? $"{o.GetType().Name}: {alter.Table}.{alter.Name} [{alter.OldColumn.ColumnType}/{alter.OldColumn.MaxLength}/{alter.OldColumn.IsNullable}] -> [{alter.ColumnType}/{alter.MaxLength}/{alter.IsNullable}]"
                    : o.GetType().Name + ": " + o)));

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
