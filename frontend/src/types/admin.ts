/** TypeScript mirrors of the shop-panel API shapes. */

export interface DashboardStats {
  salesToday: number;
  salesSameDayLastWeek: number;
  /** Null when last week was zero — "+∞%" is not something a shopkeeper can act on. */
  salesChangePercent: number | null;
  ordersOpen: number;
  ordersAwaitingMeasurements: number;
  jobsOnFloor: number;
  jobsDueTomorrow: number;
  monthToDateSales: number;
}

export interface SalesWeek { weekStart: string; sales: number; orderCount: number; }

export interface BestSeller {
  productId: number; name: string; soldCount: number; revenue: number;
  swatchColorValue?: string | null; swatchWeave?: string | null;
}

export interface OrderSummary {
  orderId: number; reference: string; placedAt: string;
  customerName: string; phone?: string | null;
  itemSummary?: string | null; lineCount: number;
  total: number; paymentMethod: string; status: string;
}

export interface Dashboard {
  stats: DashboardStats;
  salesChart: SalesWeek[];
  bestSellers: BestSeller[];
  latestOrders: OrderSummary[];
}

export interface OrderList {
  items: OrderSummary[];
  totalCount: number;
  needsAttentionCount: number;
  page: number; pageSize: number; totalPages: number;
}

export interface OrderLineDetail {
  orderLineId: number; productId: number; productName: string;
  swatchName?: string | null; swatchColorValue?: string | null; swatchWeave?: string | null;
  bedSize?: string | null; unitPrice: number; quantity: number; lineTotal: number;
}

export interface Measurement {
  bedWidthIn?: number | null; bedLengthIn?: number | null;
  windowDropIn?: number | null; windowCount?: number | null;
  notes?: string | null; takenBy?: string | null; takenAt: string;
}

export interface StatusChange {
  fromStatus?: string | null; toStatus: string;
  note?: string | null; changedBy?: string | null; changedAt: string;
}

export interface OrderDetail {
  orderId: number; reference: string; status: string; paymentMethod: string;
  subtotal: number; deliveryCharge: number; total: number;
  deliveryAddress: string; notes?: string | null; placedAt: string;
  customerId: number; customerName: string; phone?: string | null; area?: string | null;
  lines: OrderLineDetail[];
  measurements: Measurement[];
  history: StatusChange[];
}

export interface InventoryRow {
  productId: number; name: string; categoryName: string;
  price: number; stock: number; lowStockAt: number;
  /** Decided by the database so every screen agrees on what "low" means. */
  stockLabel: string;
  swatchColorValue?: string | null; swatchWeave?: string | null;
}

export interface Inventory { items: InventoryRow[]; productCount: number; lowStockCount: number; }

export interface StitchingJob {
  stitchingJobId: number; orderId: number; reference: string;
  title: string; stage: string; assignedTo?: string | null;
  swatchColorValue?: string | null; swatchWeave?: string | null;
  dueDate?: string | null; isOverdue: boolean;
}

export interface StitchingColumn { stage: string; count: number; jobs: StitchingJob[]; }
export interface StitchingBoard { columns: StitchingColumn[]; }

export interface Customer {
  customerId: number; name: string; phone: string; area?: string | null;
  orderCount: number; totalSpent: number; lastOrderAt?: string | null;
}

export interface AdminProduct {
  productId: number; name: string; slug: string;
  categoryId: number; categoryName: string;
  kicker?: string | null; blurb?: string | null;
  price: number; pieces?: string | null;
  stitchingDays: number; stock: number; lowStockAt: number;
  soldCount: number; isActive: boolean;
  defaultSwatchId?: number | null;
  swatchColorValue?: string | null; swatchWeave?: string | null;

  /** Set when the product is archived. Null while it is in the shop. */
  deletedAt?: string | null;
  /** Who archived it, by name. */
  deletedBy?: string | null;
}

export interface ShopSettings {
  shopName: string; city: string; addressLine: string; whatsAppNumber: string;
  bannerText?: string | null; openingHours?: string | null;
  deliveryCharge: number; freeDeliveryThreshold: number;
  cashOnDeliveryEnabled: boolean; whatsAppOrdersEnabled: boolean;
  reserveInShopEnabled: boolean; cardPaymentEnabled: boolean;
  updatedAt?: string | null; updatedBy?: string | null;
}

export interface LoginChallenge {
  requiresTwoFactor: boolean;
  mfaToken: string;
  isTwoFactorEnrolled: boolean;
}

export interface AuthResult {
  accessToken: string; expiresAt: string; tokenType: string;
  userId: string; email: string; fullName: string; roles: string[];
}

export interface TwoFactorSetup { sharedKey: string; authenticatorUri: string; }
export interface RecoveryCodes { recoveryCodes: string[]; }

export interface PagedAdmin<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
