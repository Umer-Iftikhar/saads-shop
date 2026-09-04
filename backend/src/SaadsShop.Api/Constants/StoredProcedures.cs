namespace SaadsShop.Api.Constants;

/// <summary>
/// Every stored procedure the application calls, in one place.
/// </summary>
/// <remarks>
/// Referenced only from repositories. Keeping the names here means renaming a
/// procedure is a compile error at one line rather than a runtime failure
/// discovered by whoever next hits that screen, and an integration test can
/// walk this class and assert each name exists in the database.
/// </remarks>
public static class StoredProcedures
{
    // ── Catalogue ──────────────────────────────────────────────────────────
    public const string CategoryGetAll = "usp_Category_GetAll";
    public const string SwatchGetAll   = "usp_Swatch_GetAll";
    public const string BedSizeGetAll  = "usp_BedSize_GetAll";
    public const string ProductGetList = "usp_Product_GetList";
    public const string ProductGetById = "usp_Product_GetById";
    public const string ProductCreate  = "usp_Product_Create";
    public const string ProductUpdate  = "usp_Product_Update";
    public const string ProductDelete  = "usp_Product_Delete";

    // ── Orders ─────────────────────────────────────────────────────────────
    public const string OrderCreate           = "usp_Order_Create";
    public const string OrderGetByReference   = "usp_Order_GetByReference";
    public const string OrderGetList          = "usp_Order_GetList";
    public const string OrderGetById          = "usp_Order_GetById";
    public const string OrderUpdateStatus     = "usp_Order_UpdateStatus";
    public const string OrderSaveMeasurements = "usp_Order_SaveMeasurements";
    public const string SetBuilderQuote       = "usp_SetBuilder_Quote";

    // ── Operations ─────────────────────────────────────────────────────────
    public const string InventoryGetList   = "usp_Inventory_GetList";
    public const string ProductAdjustStock = "usp_Product_AdjustStock";
    public const string StitchingQueueGet  = "usp_StitchingQueue_Get";
    public const string StitchingJobCreate = "usp_StitchingJob_Create";
    public const string StitchingJobUpdate = "usp_StitchingJob_Update";
    public const string CustomerGetList    = "usp_Customer_GetList";

    // ── Shop ───────────────────────────────────────────────────────────────
    public const string SettingsGetPublic = "usp_Settings_GetPublic";
    public const string SettingsGet       = "usp_Settings_Get";
    public const string SettingsUpdate    = "usp_Settings_Update";
    public const string DashboardGet      = "usp_Dashboard_Get";

    // ── Identity ───────────────────────────────────────────────────────────
    public const string UserGet            = "usp_User_Get";
    public const string UserCreate         = "usp_User_Create";
    public const string UserUpdate         = "usp_User_Update";
    public const string StaffGetList       = "usp_Staff_GetList";
    public const string RoleSetForUser     = "usp_Role_SetForUser";
    public const string UserLoginAdd       = "usp_UserLogin_Add";
    public const string UserLoginRemove    = "usp_UserLogin_Remove";
    public const string UserTokenSet       = "usp_UserToken_Set";
    public const string UserTokenGet       = "usp_UserToken_Get";
    public const string UserTokenRemove    = "usp_UserToken_Remove";
    public const string RefreshTokenCreate = "usp_RefreshToken_Create";
    public const string RefreshTokenRedeem = "usp_RefreshToken_Redeem";
    public const string RefreshTokenRevoke = "usp_RefreshToken_Revoke";
    public const string RefreshTokenPurge  = "usp_RefreshToken_Purge";
    public const string RecoveryCodeAdd    = "usp_RecoveryCode_Add";
    public const string RecoveryCodeRedeem = "usp_RecoveryCode_Redeem";
}
