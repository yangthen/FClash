namespace FClash.Domain;

public static class ConfigTransaction
{
    /// <summary>Validate before replacing the live YAML; restore and reapply it on failure.</summary>
    public static async Task ApplyAsync(string target, string yaml, Func<string, Task> validate, Func<Task> apply)
    {
        var candidate = target + ".candidate";
        var previous = File.Exists(target) ? await File.ReadAllTextAsync(target) : null;
        try
        {
            await StateStore.AtomicWriteAsync(candidate, yaml);
            await validate(candidate);
            await StateStore.AtomicWriteAsync(target, yaml);
            try { await apply(); }
            catch (Exception failure)
            {
                if (previous != null)
                {
                    await StateStore.AtomicWriteAsync(target, previous);
                    try { await apply(); }
                    catch (Exception rollback) { throw new AggregateException("配置应用及回滚均失败", failure, rollback); }
                }
                else File.Delete(target);
                throw;
            }
        }
        finally { if (File.Exists(candidate)) File.Delete(candidate); }
    }
}
