/// <summary>
/// 使用文件名作为默认 Address 的规则实现。
/// </summary>
public sealed class AddressByFileName : IAddressRule
{
    #region Public Methods

    /// <summary>
    /// 基于资源路径生成默认 Address。
    /// 首阶段不在单条规则内做全局冲突消解；冲突升级由后续批处理或校验阶段处理。
    /// </summary>
    public string GetAddress(AddressRuleContext ctx)
    {
        return AssetAddressGenerator.GenerateShortAddress(ctx.AssetPath, ctx.PrimaryType);
    }

    #endregion
}
