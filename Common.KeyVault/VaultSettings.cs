namespace Common.KeyVault
{
    /// <summary>
    /// 
    /// </summary>
    public class VaultSettings
    {
        public string Name { get; set; }
        public string ClientId { get; set; }
        public string ClientCertFile { get; set; }
        public string VaultUrl => $"https://{Name}.vault.azure.net";
    }
}