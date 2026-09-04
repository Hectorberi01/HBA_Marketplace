import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

void main() {
  runApp(const HbaSellerApp());
}

const _green = Color(0xFF00A86B);
const _orange = Color(0xFFF97316);
const _red = Color(0xFFEF4444);
const _blue = Color(0xFF2563EB);
const _text = Color(0xFF111827);
const _muted = Color(0xFF667085);
const _bg = Color(0xFFF6F7F9);
const _border = Color(0xFFE5E7EB);

enum ShopType { marketplace, restaurant }

enum ShopStatus { open, closed, paused, suspended }

enum ProductCategory {
  phones,
  audio,
  accessories,
  food,
  fashion,
  beauty,
  restaurantDish,
  appliances,
}

enum DemoListState { content, loading, error, empty }

class Shop {
  const Shop({
    required this.name,
    required this.type,
    required this.address,
    required this.city,
    required this.status,
    required this.orders,
    required this.revenue,
    required this.rating,
    required this.items,
  });

  final String name;
  final ShopType type;
  final String address;
  final String city;
  final ShopStatus status;
  final int orders;
  final String revenue;
  final double rating;
  final int items;

  String get statusLabel {
    return switch (status) {
      ShopStatus.open => 'Ouverte',
      ShopStatus.closed => 'Fermée',
      ShopStatus.paused => 'En pause',
      ShopStatus.suspended => 'Suspendue',
    };
  }

  Color get statusColor {
    return switch (status) {
      ShopStatus.open => _green,
      ShopStatus.closed => _muted,
      ShopStatus.paused => _orange,
      ShopStatus.suspended => _red,
    };
  }

  String get typeLabel =>
      type == ShopType.restaurant ? 'Restaurant' : 'Marketplace';
}

class Product {
  const Product({
    required this.name,
    required this.category,
    required this.price,
    required this.stock,
    required this.status,
    required this.icon,
    required this.color,
  });

  final String name;
  final String category;
  final String price;
  final int stock;
  final String status;
  final IconData icon;
  final Color color;
}

class Order {
  const Order({
    required this.id,
    required this.client,
    required this.amount,
    required this.status,
    required this.items,
    required this.time,
    required this.address,
    this.urgent = false,
  });

  final String id;
  final String client;
  final String amount;
  final String status;
  final int items;
  final String time;
  final String address;
  final bool urgent;
}

class Conversation {
  const Conversation({
    required this.name,
    required this.message,
    required this.time,
    required this.unread,
    this.orderId,
  });

  final String name;
  final String message;
  final String time;
  final int unread;
  final String? orderId;
}

const shops = [
  Shop(
    name: 'Awa Électronique',
    type: ShopType.marketplace,
    address: 'Akpakpa',
    city: 'Cotonou',
    status: ShopStatus.open,
    orders: 8,
    revenue: '487 000 FCFA',
    rating: 4.7,
    items: 42,
  ),
  Shop(
    name: 'Awa Accessoires',
    type: ShopType.marketplace,
    address: 'Cotonou Centre',
    city: 'Cotonou',
    status: ShopStatus.open,
    orders: 5,
    revenue: '213 000 FCFA',
    rating: 4.8,
    items: 67,
  ),
  Shop(
    name: 'Restaurant Chez Awa',
    type: ShopType.restaurant,
    address: 'Porto-Novo Centre',
    city: 'Porto-Novo',
    status: ShopStatus.paused,
    orders: 0,
    revenue: '0 FCFA',
    rating: 4.3,
    items: 18,
  ),
];

const products = [
  Product(
    name: 'Samsung Galaxy A35',
    category: 'Téléphones',
    price: '165 000 FCFA',
    stock: 12,
    status: 'Actif',
    icon: Icons.smartphone_rounded,
    color: _blue,
  ),
  Product(
    name: 'Écouteurs JBL Tune',
    category: 'Audio',
    price: '28 500 FCFA',
    stock: 3,
    status: 'Stock faible',
    icon: Icons.headphones_rounded,
    color: _orange,
  ),
  Product(
    name: 'Riz local 25 kg',
    category: 'Alimentaire',
    price: '14 200 FCFA',
    stock: 0,
    status: 'Rupture',
    icon: Icons.rice_bowl_rounded,
    color: _red,
  ),
  Product(
    name: 'Huile palme 5L',
    category: 'Alimentaire',
    price: '5 800 FCFA',
    stock: 28,
    status: 'Actif',
    icon: Icons.local_drink_rounded,
    color: _green,
  ),
];

const dishes = [
  Product(
    name: 'Riz au poulet braisé',
    category: 'Plats',
    price: '3 500 FCFA',
    stock: 18,
    status: 'Disponible',
    icon: Icons.restaurant_rounded,
    color: _orange,
  ),
  Product(
    name: 'Attiéké poisson',
    category: 'Plats',
    price: '4 200 FCFA',
    stock: 9,
    status: 'Disponible',
    icon: Icons.set_meal_rounded,
    color: _blue,
  ),
  Product(
    name: 'Jus de bissap',
    category: 'Boissons',
    price: '1 000 FCFA',
    stock: 0,
    status: 'Indisponible',
    icon: Icons.local_cafe_rounded,
    color: _red,
  ),
];

const orders = [
  Order(
    id: 'CMD-2847',
    client: 'Kofi Mensah',
    amount: '12 500 FCFA',
    status: 'Nouvelle',
    items: 3,
    time: '10 min',
    address: 'Rue des Palmiers, Cotonou',
    urgent: true,
  ),
  Order(
    id: 'CMD-2846',
    client: 'Aïcha Traoré',
    amount: '8 200 FCFA',
    status: 'Préparation',
    items: 2,
    time: '22 min',
    address: 'Quartier Zongo, Calavi',
  ),
  Order(
    id: 'CMD-2844',
    client: 'Rachid Alabi',
    amount: '6 500 FCFA',
    status: 'Prête',
    items: 1,
    time: '48 min',
    address: 'Akpakpa centre, Cotonou',
  ),
];

const conversations = [
  Conversation(
    name: 'Fatou Bio',
    message: 'Bonjour, le produit est-il disponible ?',
    time: '2 min',
    unread: 2,
  ),
  Conversation(
    name: 'Mariam D.',
    message: 'Pouvez-vous changer l’adresse ?',
    time: '18 min',
    unread: 1,
    orderId: 'CMD-2841',
  ),
  Conversation(
    name: 'Jean A.',
    message: 'Merci, j’ai reçu ma commande.',
    time: '1 h',
    unread: 0,
  ),
];

class HbaSellerApp extends StatefulWidget {
  const HbaSellerApp({super.key});

  @override
  State<HbaSellerApp> createState() => _HbaSellerAppState();
}

class _HbaSellerAppState extends State<HbaSellerApp> {
  var activeShop = shops.first;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'HBAExpress Pro',
      theme: ThemeData(
        scaffoldBackgroundColor: _bg,
        colorScheme: ColorScheme.fromSeed(seedColor: _green),
        textTheme: GoogleFonts.interTextTheme().apply(
          bodyColor: _text,
          displayColor: _text,
        ),
        useMaterial3: true,
      ),
      home: SellerShell(
        activeShop: activeShop,
        onShopChanged: (shop) => setState(() => activeShop = shop),
      ),
    );
  }
}

class SellerShell extends StatefulWidget {
  const SellerShell({
    super.key,
    required this.activeShop,
    required this.onShopChanged,
  });

  final Shop activeShop;
  final ValueChanged<Shop> onShopChanged;

  @override
  State<SellerShell> createState() => _SellerShellState();
}

class _SellerShellState extends State<SellerShell> {
  int tab = 0;

  @override
  Widget build(BuildContext context) {
    final isRestaurant = widget.activeShop.type == ShopType.restaurant;
    final pages = [
      DashboardScreen(
        activeShop: widget.activeShop,
        onSelectShop: _openShopSheet,
      ),
      OrdersScreen(isRestaurant: isRestaurant),
      isRestaurant ? RestaurantMenuScreen() : const CatalogScreen(),
      const FinanceScreen(),
      MoreScreen(activeShop: widget.activeShop, onSelectShop: _openShopSheet),
    ];

    return Scaffold(
      body: SafeArea(child: pages[tab]),
      bottomNavigationBar: NavigationBar(
        selectedIndex: tab,
        onDestinationSelected: (value) => setState(() => tab = value),
        backgroundColor: Colors.white,
        indicatorColor: _green.withValues(alpha: 0.12),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home_rounded),
            label: 'Accueil',
          ),
          NavigationDestination(
            icon: Badge.count(
              count: 2,
              backgroundColor: _red,
              child: const Icon(Icons.receipt_long_outlined),
            ),
            selectedIcon: const Icon(Icons.receipt_long_rounded),
            label: 'Commandes',
          ),
          NavigationDestination(
            icon: const Icon(Icons.grid_view_outlined),
            selectedIcon: const Icon(Icons.grid_view_rounded),
            label: isRestaurant ? 'Menu' : 'Catalogue',
          ),
          const NavigationDestination(
            icon: Icon(Icons.attach_money_rounded),
            selectedIcon: Icon(Icons.account_balance_wallet_rounded),
            label: 'Finance',
          ),
          const NavigationDestination(
            icon: Icon(Icons.more_vert_rounded),
            selectedIcon: Icon(Icons.more_vert_rounded),
            label: 'Plus',
          ),
        ],
      ),
    );
  }

  void _openShopSheet() {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (context) => ShopSelectorSheet(
        activeShop: widget.activeShop,
        onSelected: (shop) {
          widget.onShopChanged(shop);
          Navigator.pop(context);
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Boutique changée : ${shop.name}')),
          );
        },
      ),
    );
  }
}

class DashboardScreen extends StatelessWidget {
  const DashboardScreen({
    super.key,
    required this.activeShop,
    required this.onSelectShop,
  });

  final Shop activeShop;
  final VoidCallback onSelectShop;

  @override
  Widget build(BuildContext context) {
    final restaurant = activeShop.type == ShopType.restaurant;
    if (restaurant) {
      return RestaurantHomeScreen(
        activeShop: activeShop,
        onSelectShop: onSelectShop,
      );
    }

    return MarketplaceHomeScreen(
      activeShop: activeShop,
      onSelectShop: onSelectShop,
    );
  }
}

class MarketplaceHomeScreen extends StatelessWidget {
  const MarketplaceHomeScreen({
    super.key,
    required this.activeShop,
    required this.onSelectShop,
  });

  final Shop activeShop;
  final VoidCallback onSelectShop;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        HomeHeader(
          greeting: 'Bonjour 👋 Amidou',
          shop: activeShop,
          onSelectShop: onSelectShop,
          onNotifications: () => pushPage(context, const NotificationsScreen()),
        ),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
            children: [
              HbaCard(
                child: Row(
                  children: [
                    const IconBox(icon: Icons.home_outlined, color: _green),
                    const SizedBox(width: 14),
                    const Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Awa Électronique',
                            style: TextStyle(color: _muted),
                          ),
                          Text(
                            'Boutique ouverte',
                            style: TextStyle(
                              color: _green,
                              fontWeight: FontWeight.w900,
                              fontSize: 18,
                            ),
                          ),
                        ],
                      ),
                    ),
                    Switch(
                      value: true,
                      activeThumbColor: _green,
                      onChanged: (_) {},
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
              const HomeStatsGrid(
                cards: [
                  HomeStatCard(label: "Commandes aujourd'hui", value: '14'),
                  HomeStatCard(label: 'CA du jour', value: '87 400 F'),
                  HomeStatCard(label: 'En attente', value: '3'),
                  HomeStatCard(label: 'Note moyenne', value: '4.7 ★'),
                ],
              ),
              const SizedBox(height: 18),
              const SmallSectionTitle('ALERTES'),
              AlertRow(
                icon: Icons.assignment_outlined,
                text: '3 commandes à traiter',
                color: _blue,
                background: const Color(0xFFEFF6FF),
                onTap: () {},
              ),
              AlertRow(
                icon: Icons.inventory_2_outlined,
                text: '2 produits en rupture de stock',
                color: const Color(0xFFEA580C),
                background: const Color(0xFFFFF7ED),
                onTap: () {},
              ),
              AlertRow(
                icon: Icons.credit_card_outlined,
                text: '45 000 FCFA disponibles',
                color: const Color(0xFF047857),
                background: const Color(0xFFECFDF5),
                onTap: () {},
              ),
              const SizedBox(height: 18),
              const SmallSectionTitle('ACTIONS RAPIDES'),
              Row(
                children: [
                  Expanded(
                    child: QuickAction(
                      icon: Icons.add_rounded,
                      label: 'Produit',
                      color: _green,
                      onTap: () => pushPage(context, const ProductFormScreen()),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.assignment_outlined,
                      label: 'Commandes',
                      color: _blue,
                      onTap: () {},
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.arrow_upward_rounded,
                      label: 'Retrait',
                      color: _orange,
                      onTap: () {},
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.star_border_rounded,
                      label: 'Avis',
                      color: Colors.purple,
                      onTap: () => pushPage(context, const ReviewsScreen()),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 18),
              SectionHeader(
                title: 'COMMANDES RÉCENTES',
                action: 'Voir tout →',
                onTap: () {},
              ),
              ...orders.map((order) => RecentOrderTile(order: order)),
            ],
          ),
        ),
      ],
    );
  }
}

class RestaurantHomeScreen extends StatelessWidget {
  const RestaurantHomeScreen({
    super.key,
    required this.activeShop,
    required this.onSelectShop,
  });

  final Shop activeShop;
  final VoidCallback onSelectShop;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        HomeHeader(
          greeting: 'Cuisine 🍽️ Amidou',
          shop: activeShop,
          onSelectShop: onSelectShop,
          onNotifications: () => pushPage(context, const NotificationsScreen()),
        ),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
            children: [
              const RestaurantStatusCard(),
              const SizedBox(height: 12),
              Row(
                children: const [
                  Expanded(
                    child: RestaurantMetricCard(
                      Icons.assignment_outlined,
                      '12',
                      'Commandes',
                      _orange,
                    ),
                  ),
                  SizedBox(width: 8),
                  Expanded(
                    child: RestaurantMetricCard(
                      Icons.credit_card_outlined,
                      '42 500',
                      'Revenus',
                      _green,
                    ),
                  ),
                  SizedBox(width: 8),
                  Expanded(
                    child: RestaurantMetricCard(
                      Icons.inventory_2_outlined,
                      '28',
                      'Plats vendus',
                      _blue,
                    ),
                  ),
                  SizedBox(width: 8),
                  Expanded(
                    child: RestaurantMetricCard(
                      Icons.star_border_rounded,
                      '4.8★',
                      'Note',
                      Colors.amber,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              SectionHeader(
                title: 'Commandes urgentes',
                action: 'Tout voir →',
                onTap: () {},
              ),
              const FoodUrgentCard(
                id: '#FOOD-2841',
                detail: '2× Riz au poulet braisé, 2× Jus de bissap',
                status: 'En préparation',
                amount: '8 000 FCFA',
                eta: '8 min',
              ),
              const FoodUrgentCard(
                id: '#FOOD-2843',
                detail: '1× Riz au poulet braisé, 1× Salade de crudités',
                status: 'À accepter',
                amount: '4 700 FCFA',
                eta: '25 min',
              ),
              const SizedBox(height: 8),
              const AvailabilityCard(),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: QuickAction(
                      icon: Icons.add_rounded,
                      label: 'Ajouter plat',
                      color: _green,
                      onTap: () => pushPage(context, const DishFormScreen()),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.grid_view_rounded,
                      label: 'Gérer menu',
                      color: _blue,
                      onTap: () {},
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.assignment_outlined,
                      label: 'Cuisine',
                      color: _orange,
                      onTap: () => pushPage(context, const KitchenScreen()),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: QuickAction(
                      icon: Icons.schedule_rounded,
                      label: 'Mettre en pause',
                      color: _red,
                      onTap: () {},
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class CatalogScreen extends StatefulWidget {
  const CatalogScreen({super.key});

  @override
  State<CatalogScreen> createState() => _CatalogScreenState();
}

class _CatalogScreenState extends State<CatalogScreen> {
  DemoListState state = DemoListState.content;

  @override
  Widget build(BuildContext context) {
    return HbaPage(
      title: 'Catalogue',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 10, 20, 24),
        children: [
          Row(
            children: [
              const Expanded(
                child: SearchAndFilter(
                  hint: 'Rechercher un produit...',
                  filter: false,
                ),
              ),
              const SizedBox(width: 10),
              IconButton.filled(
                style: IconButton.styleFrom(
                  backgroundColor: _green,
                  foregroundColor: Colors.white,
                  minimumSize: const Size(48, 48),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                  ),
                ),
                onPressed: () => pushPage(context, const ProductFormScreen()),
                icon: const Icon(Icons.add_rounded),
              ),
            ],
          ),
          const SizedBox(height: 8),
          StateChips(
            state: state,
            onChanged: (value) => setState(() => state = value),
          ),
          const SizedBox(height: 12),
          const FilterChips(
            labels: ['Tous', 'Actifs', 'Stock faible', 'Rupture'],
          ),
          const SizedBox(height: 16),
          if (state == DemoListState.loading) const SkeletonList(itemCount: 4),
          if (state == DemoListState.error)
            ErrorStateView(
              title: 'Erreur de connexion',
              message: 'Vérifiez votre connexion internet.',
              onRetry: () => setState(() => state = DemoListState.content),
            ),
          if (state == DemoListState.empty)
            EmptyStateView(
              icon: Icons.inventory_2_outlined,
              title: 'Aucun produit',
              message: 'Ajoutez votre premier produit.',
              actionLabel: 'Ajouter un produit',
              onAction: () => pushPage(context, const ProductFormScreen()),
            ),
          if (state == DemoListState.content)
            ...products.map(
              (product) => ProductCard(
                product: product,
                onTap: () =>
                    pushPage(context, ProductDetailScreen(product: product)),
                onEdit: () =>
                    pushPage(context, ProductFormScreen(product: product)),
              ),
            ),
        ],
      ),
    );
  }
}

class RestaurantMenuScreen extends StatelessWidget {
  const RestaurantMenuScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaPage(
      title: 'Menu',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 10, 20, 24),
        children: [
          Row(
            children: [
              const Expanded(
                child: SearchAndFilter(
                  hint: 'Rechercher un plat...',
                  filter: false,
                ),
              ),
              const SizedBox(width: 10),
              FilledButton.icon(
                style: FilledButton.styleFrom(
                  backgroundColor: _green,
                  foregroundColor: Colors.white,
                  minimumSize: const Size(96, 48),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                  ),
                ),
                onPressed: () => pushPage(context, const DishFormScreen()),
                icon: const Icon(Icons.add_rounded),
                label: const Text(
                  'Ajouter',
                  style: TextStyle(fontWeight: FontWeight.w900),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          const FilterChips(
            labels: ['Tous', 'Disponibles', 'Indisponibles', 'Populaires'],
            activeColor: _orange,
          ),
          const SizedBox(height: 12),
          const FilterChips(
            labels: [
              'Tous',
              'Entrée',
              'Plat',
              'Accompagnement',
              'Boisson',
              'Dessert',
            ],
            activeColor: _text,
          ),
          const SizedBox(height: 16),
          RestaurantDishCard(
            name: 'Riz au poulet braisé',
            meta: 'Plat · 20 min',
            price: '3500 FCFA',
            status: 'Dispo',
            color: _green,
            imageColor: const Color(0xFF166534),
            onTap: () =>
                pushPage(context, DishDetailScreen(dish: dishes.first)),
          ),
          RestaurantDishCard(
            name: 'Attiéké poisson grillé',
            meta: 'Plat · 25 min',
            price: '2800 FCFA',
            status: 'Dispo',
            color: _green,
            imageColor: const Color(0xFF365314),
            onTap: () => pushPage(context, DishDetailScreen(dish: dishes[1])),
          ),
          RestaurantDishCard(
            name: 'Pâte rouge et légumes',
            meta: 'Plat · 15 min',
            price: '1800 FCFA',
            status: 'Indispo',
            color: _red,
            imageColor: const Color(0xFF92400E),
            onTap: () =>
                pushPage(context, DishDetailScreen(dish: dishes.first)),
          ),
          RestaurantDishCard(
            name: 'Alloco banane plantain',
            meta: 'Accompagnement · 10 min',
            price: '800 FCFA',
            status: 'Dispo',
            color: _green,
            imageColor: const Color(0xFFB45309),
            onTap: () =>
                pushPage(context, DishDetailScreen(dish: dishes.first)),
          ),
          RestaurantDishCard(
            name: 'Jus de bissap',
            meta: 'Boisson · 5 min',
            price: '500 FCFA',
            status: 'Dispo',
            color: _green,
            imageColor: const Color(0xFFBE123C),
            onTap: () => pushPage(context, DishDetailScreen(dish: dishes.last)),
          ),
        ],
      ),
    );
  }
}

class OrdersScreen extends StatefulWidget {
  const OrdersScreen({super.key, required this.isRestaurant});

  final bool isRestaurant;

  @override
  State<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends State<OrdersScreen> {
  DemoListState state = DemoListState.content;

  @override
  Widget build(BuildContext context) {
    if (widget.isRestaurant) {
      return HbaPage(
        title: 'Commandes',
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 10, 20, 24),
          children: const [
            FilterChips(
              labels: [
                'Toutes',
                'À accepter',
                'Acceptées',
                'En prépa.',
                'Prêtes',
                'Livrées',
              ],
              activeColor: _orange,
            ),
            SizedBox(height: 16),
            FoodOrderCard(
              id: '#FOOD-2841',
              client: 'Fatou Bio',
              type: 'Livraison',
              time: '12:03',
              items: '2× Riz au poulet braisé, 2× Jus de bissap',
              status: 'En préparation',
              amount: '8 000 F',
              eta: '8 min',
              highlighted: true,
              action: 'Marquer prête',
            ),
            FoodOrderCard(
              id: '#FOOD-2842',
              client: 'Jean Ahouansou',
              type: 'Retrait',
              time: '12:08',
              items: '1× Attiéké poisson grillé, 1× Alloco banane plantain',
              status: 'Acceptée',
              amount: '3 600 F',
              eta: '17 min',
              action: 'Préparer',
              statusColor: _blue,
            ),
            FoodOrderCard(
              id: '#FOOD-2843',
              client: 'Mariam Dossou',
              type: 'Livraison',
              time: '12:15',
              items: '1× Riz au poulet braisé, 1× Salade de crudités',
              status: 'À accepter',
              amount: '4 700 F',
              eta: '25 min',
              action: 'Accepter',
              statusColor: _orange,
            ),
            FoodOrderCard(
              id: '#FOOD-2844',
              client: 'Koffi Mensah',
              type: 'Retrait',
              time: '11:55',
              items: '3× Jus de bissap',
              status: 'Prête',
              amount: '1 500 F',
              eta: '',
              action: 'Récupérée',
              statusColor: _green,
            ),
          ],
        ),
      );
    }

    return HbaPage(
      title: 'Commandes',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 10, 20, 24),
        children: [
          const SearchAndFilter(hint: 'Rechercher...'),
          const SizedBox(height: 8),
          StateChips(
            state: state,
            onChanged: (value) => setState(() => state = value),
          ),
          const SizedBox(height: 12),
          const FilterChips(
            labels: [
              'Nouvelles 2',
              'Acceptées',
              'Préparation',
              'Prêtes',
              'Terminées',
              'Annulées',
            ],
          ),
          const SizedBox(height: 16),
          if (state == DemoListState.loading) const SkeletonList(itemCount: 3),
          if (state == DemoListState.error)
            ErrorStateView(
              title: 'Erreur de connexion',
              message: 'Vérifiez votre connexion internet.',
              onRetry: () => setState(() => state = DemoListState.content),
            ),
          if (state == DemoListState.empty)
            const EmptyStateView(
              icon: Icons.inbox_outlined,
              title: 'Aucune commande',
              message: 'Aucune commande "Nouvelles" pour le moment.',
            ),
          if (state == DemoListState.content)
            ...orders
                .take(2)
                .map(
                  (order) => MarketplaceOrderCard(
                    order: order,
                    onTap: () => pushPage(
                      context,
                      OrderDetailScreen(order: order, restaurant: false),
                    ),
                  ),
                ),
        ],
      ),
    );
  }
}

class FinanceScreen extends StatelessWidget {
  const FinanceScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaPage(
      title: 'Finance',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 24),
        children: [
          FinanceBalanceCard(
            onWithdraw: () => showModalBottomSheet<void>(
              context: context,
              isScrollControlled: true,
              backgroundColor: Colors.white,
              shape: const RoundedRectangleBorder(
                borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
              ),
              builder: (context) => const WithdrawSheet(),
            ),
          ),
          const SizedBox(height: 12),
          Row(
            children: const [
              Expanded(
                child: FinanceMiniCard(
                  label: 'Commissions',
                  value: '-4 200 F',
                  color: _red,
                ),
              ),
              SizedBox(width: 12),
              Expanded(
                child: FinanceMiniCard(
                  label: 'Remboursements',
                  value: '-7 800 F',
                  color: _red,
                ),
              ),
              SizedBox(width: 12),
              Expanded(
                child: FinanceMiniCard(
                  label: 'Prochain virt.',
                  value: 'Dans 3j',
                  color: _blue,
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
          SectionHeader(
            title: 'Transactions',
            action: 'Filtrer',
            onTap: () => pushPage(context, const InvoicesScreen()),
          ),
          const TransactionTile(
            'Paiement commande CMD-2844',
            '+6 500 FCFA',
            'Auj. 14:22',
            _green,
            incoming: true,
          ),
          const TransactionTile(
            'Commission HBAExpress',
            '-520 FCFA',
            'Auj. 14:22',
            _red,
            incoming: false,
          ),
          const TransactionTile(
            'Paiement commande CMD-2843',
            '+19 000 FCFA',
            'Auj. 11:05',
            _green,
            incoming: true,
          ),
          const TransactionTile(
            'Retrait Mobile Money MTN',
            '-50 000 FCFA',
            'Hier 18:30',
            _red,
            incoming: false,
          ),
        ],
      ),
    );
  }
}

class MoreScreen extends StatelessWidget {
  const MoreScreen({
    super.key,
    required this.activeShop,
    required this.onSelectShop,
  });

  final Shop activeShop;
  final VoidCallback onSelectShop;

  @override
  Widget build(BuildContext context) {
    return HbaPage(
      title: 'Menu',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          HbaCard(
            child: Row(
              children: [
                const IconBox(
                  icon: Icons.person_outline,
                  color: _green,
                  size: 56,
                ),
                const SizedBox(width: 14),
                const Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Amidou Kossou', style: _titleStyle),
                      Text(
                        'amidou@awaelectronique.bj',
                        style: TextStyle(color: _muted),
                      ),
                      Text('Propriétaire', style: TextStyle(color: _muted)),
                    ],
                  ),
                ),
                IconButton(
                  onPressed: () => pushPage(context, const ProfileScreen()),
                  icon: const Icon(Icons.edit_outlined),
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          HbaCard(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                MenuRow(
                  Icons.storefront_outlined,
                  _green,
                  'Gestion des boutiques',
                  '${activeShop.name} · ${activeShop.statusLabel}',
                  onSelectShop,
                ),
                MenuRow(
                  Icons.chat_bubble_outline,
                  _blue,
                  'Messages',
                  '3 conversations non lues',
                  () => pushPage(context, const MessagesScreen()),
                ),
                MenuRow(
                  Icons.star_border_rounded,
                  Colors.amber,
                  'Avis et notes',
                  '4.7 ★ · 128 avis',
                  () => pushPage(context, const ReviewsScreen()),
                ),
                MenuRow(
                  Icons.local_shipping_outlined,
                  _blue,
                  'Livraisons',
                  '2 en cours',
                  () => pushPage(context, const DeliveriesScreen()),
                ),
                MenuRow(
                  Icons.group_outlined,
                  Colors.purple,
                  'Membres & rôles',
                  '3 membres actifs',
                  () => pushPage(context, const MembersScreen()),
                ),
                MenuRow(
                  Icons.notifications_none_rounded,
                  _orange,
                  'Notifications',
                  '5 non lues',
                  () => pushPage(context, const NotificationsScreen()),
                ),
                MenuRow(
                  Icons.help_outline_rounded,
                  _muted,
                  'Support',
                  'Centre d’aide & tickets',
                  () => pushPage(context, const SupportScreen()),
                ),
                MenuRow(
                  Icons.settings_outlined,
                  _muted,
                  'Paramètres',
                  'Compte, sécurité, langue',
                  () => pushPage(context, const SettingsScreen()),
                ),
              ],
            ),
          ),
          const SizedBox(height: 18),
          OutlinedButton.icon(
            style: OutlinedButton.styleFrom(
              foregroundColor: _red,
              side: BorderSide(color: _red.withValues(alpha: 0.35)),
              minimumSize: const Size.fromHeight(56),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(14),
              ),
            ),
            onPressed: () {},
            icon: const Icon(Icons.logout_rounded),
            label: const Text(
              'Se déconnecter',
              style: TextStyle(fontWeight: FontWeight.w800),
            ),
          ),
        ],
      ),
    );
  }
}

class ProductDetailScreen extends StatelessWidget {
  const ProductDetailScreen({super.key, required this.product});

  final Product product;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _bg,
      body: SafeArea(
        child: Column(
          children: [
            const FormTopBar(title: 'Détail produit'),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
                children: [
                  ProductGallery(product: product),
                  const SizedBox(height: 14),
                  ProductSummaryCard(product: product),
                  const SizedBox(height: 14),
                  const InfoCard(
                    title: 'DESCRIPTION',
                    lines: [
                      'Le Samsung Galaxy A35 offre une expérience premium avec son écran Super AMOLED 6,6 pouces, son triple capteur photo 50 Mpx et sa batterie longue durée.',
                      'Voir plus ↓',
                    ],
                  ),
                  const SizedBox(height: 14),
                  const ProductVariantsCard(),
                  const SizedBox(height: 14),
                  const CommercialInfoCard(),
                  const SizedBox(height: 14),
                  const DeliveryInfoCard(),
                  const SizedBox(height: 14),
                  const ProductStatsCard(),
                ],
              ),
            ),
            Container(
              padding: EdgeInsets.fromLTRB(
                20,
                12,
                20,
                MediaQuery.of(context).padding.bottom + 12,
              ),
              decoration: const BoxDecoration(
                color: Colors.white,
                border: Border(top: BorderSide(color: _border)),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: HbaButton(
                      label: 'Désactiver',
                      outlined: true,
                      onPressed: () {},
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: HbaButton(
                      label: 'Modifier',
                      onPressed: () => pushPage(
                        context,
                        ProductFormScreen(product: product),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class ProductGallery extends StatelessWidget {
  const ProductGallery({super.key, required this.product});

  final Product product;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: EdgeInsets.zero,
      child: Column(
        children: [
          Stack(
            children: [
              Container(
                height: 210,
                decoration: BoxDecoration(
                  color: product.color.withValues(alpha: 0.08),
                  borderRadius: const BorderRadius.vertical(
                    top: Radius.circular(18),
                  ),
                ),
                child: Center(
                  child: Icon(product.icon, size: 92, color: product.color),
                ),
              ),
              Positioned(
                right: 14,
                top: 14,
                child: StatusBadge(
                  product.status,
                  color: _green,
                  compact: true,
                ),
              ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: List.generate(
                4,
                (index) => Container(
                  width: 54,
                  height: 54,
                  margin: const EdgeInsets.only(right: 10),
                  decoration: BoxDecoration(
                    color: [
                      product.color,
                      const Color(0xFFFBBF24),
                      const Color(0xFF92400E),
                      _text,
                    ][index].withValues(alpha: 0.18),
                    borderRadius: BorderRadius.circular(10),
                    border: Border.all(
                      color: index == 0 ? _green : Colors.transparent,
                      width: 1.5,
                    ),
                  ),
                  child: Icon(
                    index == 0 ? product.icon : Icons.smartphone_rounded,
                    color: index == 0 ? product.color : _text,
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class ProductSummaryCard extends StatelessWidget {
  const ProductSummaryCard({super.key, required this.product});

  final Product product;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  product.name,
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
              Text(
                product.price,
                style: const TextStyle(
                  color: Color(0xFF047857),
                  fontSize: 20,
                  fontWeight: FontWeight.w900,
                ),
              ),
            ],
          ),
          Text(product.category, style: const TextStyle(color: _muted)),
          const SizedBox(height: 12),
          Row(
            children: const [
              StatusBadge('12 en stock', color: _green, compact: true),
              SizedBox(width: 14),
              Icon(
                Icons.inventory_2_outlined,
                size: 14,
                color: Color(0xFF98A2B3),
              ),
              SizedBox(width: 4),
              Text(
                'SKU : AWA-TEL-A35-128',
                style: TextStyle(color: Color(0xFF98A2B3), fontSize: 12),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class ProductVariantsCard extends StatelessWidget {
  const ProductVariantsCard({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: const [
          SmallSectionTitle('VARIANTES'),
          Text('Couleur : Noir', style: TextStyle(fontWeight: FontWeight.w700)),
          SizedBox(height: 10),
          Wrap(
            spacing: 8,
            children: [
              GreenChoice(label: '● Noir', selected: true),
              GreenChoice(label: '● Bleu'),
              GreenChoice(label: '● Violet'),
            ],
          ),
          SizedBox(height: 14),
          Text(
            'Stockage : 128 Go',
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
          SizedBox(height: 10),
          Wrap(
            spacing: 8,
            children: [
              GreenChoice(label: '128 Go', selected: true),
              GreenChoice(label: '256 Go'),
            ],
          ),
        ],
      ),
    );
  }
}

class CommercialInfoCard extends StatelessWidget {
  const CommercialInfoCard({super.key});

  @override
  Widget build(BuildContext context) {
    return const InfoRowsCard(
      title: 'INFORMATIONS COMMERCIALES',
      rows: [
        ('Prix de vente', '165 000 FCFA'),
        ('Prix promotionnel', '—'),
        ("Coût d'achat", '120 000 FCFA'),
        ('Marge estimée', '37,8%'),
        ('TVA applicable', '18%'),
      ],
    );
  }
}

class DeliveryInfoCard extends StatelessWidget {
  const DeliveryInfoCard({super.key});

  @override
  Widget build(BuildContext context) {
    return const InfoRowsCard(
      title: 'LIVRAISON',
      rows: [
        ('Livrable', 'Oui'),
        ('Poids', '0,8 kg'),
        ('Dimensions', '16 × 8 × 1 cm'),
      ],
    );
  }
}

class ProductStatsCard extends StatelessWidget {
  const ProductStatsCard({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: const [
          SmallSectionTitle('STATISTIQUES'),
          Row(
            children: [
              Expanded(
                child: RestaurantMetricCard(
                  Icons.visibility_outlined,
                  '1 248',
                  'Vues',
                  _blue,
                ),
              ),
              SizedBox(width: 8),
              Expanded(
                child: RestaurantMetricCard(
                  Icons.shopping_bag_outlined,
                  '87',
                  'Paniers',
                  _orange,
                ),
              ),
              SizedBox(width: 8),
              Expanded(
                child: RestaurantMetricCard(
                  Icons.assignment_outlined,
                  '34',
                  'Commandes',
                  _green,
                ),
              ),
              SizedBox(width: 8),
              Expanded(
                child: RestaurantMetricCard(
                  Icons.star_border_rounded,
                  '4.7 ★',
                  'Note moy.',
                  Colors.amber,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class InfoRowsCard extends StatelessWidget {
  const InfoRowsCard({super.key, required this.title, required this.rows});

  final String title;
  final List<(String, String)> rows;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SmallSectionTitle(title),
          ...rows.map(
            (row) => Padding(
              padding: const EdgeInsets.symmetric(vertical: 10),
              child: Row(
                children: [
                  Expanded(
                    child: Text(row.$1, style: const TextStyle(color: _muted)),
                  ),
                  Text(
                    row.$2,
                    style: TextStyle(
                      fontWeight: FontWeight.w900,
                      color: row.$2.contains('%')
                          ? const Color(0xFF047857)
                          : _text,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class ProductFormScreen extends StatefulWidget {
  const ProductFormScreen({super.key, this.product});

  final Product? product;

  @override
  State<ProductFormScreen> createState() => _ProductFormScreenState();
}

class _ProductFormScreenState extends State<ProductFormScreen> {
  int step = 0;
  ProductCategory category = ProductCategory.phones;

  @override
  Widget build(BuildContext context) {
    final edit = widget.product != null;
    return Scaffold(
      backgroundColor: _bg,
      body: SafeArea(
        child: Column(
          children: [
            FormTopBar(title: edit ? 'Modifier produit' : 'Nouveau produit'),
            ProductFormProgress(step: step),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
                children: [
                  if (step == 0) _infoStep(),
                  if (step == 1) _priceStep(),
                  if (step == 2) const ProductPhotosStep(filled: true),
                  if (step == 3) const ProductAttributesStep(),
                  if (step == 4) const ProductDeliveryStep(),
                ],
              ),
            ),
            DishFormFooter(
              step: step,
              color: _green,
              finalLabel: 'Publier',
              onBack: () => setState(() => step -= 1),
              onNext: () async {
                if (step < 4) {
                  setState(() => step += 1);
                  return;
                }
                final published = await showDialog<bool>(
                  context: context,
                  barrierColor: Colors.black.withValues(alpha: 0.55),
                  builder: (context) => const ProductPublishDialog(),
                );
                if (!context.mounted || published != true) return;
                Navigator.pop(context);
                ScaffoldMessenger.of(
                  context,
                ).showSnackBar(const SnackBar(content: Text('Produit publié')));
              },
            ),
          ],
        ),
      ),
    );
  }

  Widget _infoStep() {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          HbaTextField(
            label: 'Nom du produit',
            hint: 'Ex: Samsung Galaxy A35',
            initial: widget.product?.name,
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<ProductCategory>(
            initialValue: category,
            decoration: inputDecoration('Catégorie principale'),
            items: const [
              DropdownMenuItem(
                value: ProductCategory.phones,
                child: Text('Téléphones'),
              ),
              DropdownMenuItem(
                value: ProductCategory.audio,
                child: Text('Audio'),
              ),
              DropdownMenuItem(
                value: ProductCategory.accessories,
                child: Text('Accessoires'),
              ),
              DropdownMenuItem(
                value: ProductCategory.food,
                child: Text('Alimentaire'),
              ),
              DropdownMenuItem(
                value: ProductCategory.fashion,
                child: Text('Mode'),
              ),
              DropdownMenuItem(
                value: ProductCategory.beauty,
                child: Text('Beauté'),
              ),
              DropdownMenuItem(
                value: ProductCategory.restaurantDish,
                child: Text('Restaurant / Plat préparé'),
              ),
              DropdownMenuItem(
                value: ProductCategory.appliances,
                child: Text('Électroménager'),
              ),
            ],
            onChanged: (value) => setState(() => category = value ?? category),
          ),
          const SizedBox(height: 14),
          const HbaTextField(
            label: 'Sous-catégorie (optionnel)',
            hint: 'Ex: Smartphones',
          ),
          const SizedBox(height: 14),
          const HbaTextField(label: 'Marque (optionnel)', hint: 'Ex: Samsung'),
          const SizedBox(height: 14),
          const HbaTextField(
            label: 'Description courte',
            hint: 'Ex: Smartphone Android 6,6" Super AMOLED',
          ),
          const SizedBox(height: 14),
          const HbaTextField(
            label: 'Description détaillée (optionnel)',
            hint: 'Décrivez votre produit en détail...',
            maxLines: 4,
          ),
        ],
      ),
    );
  }

  Widget _priceStep() {
    return const Column(
      children: [
        HbaCard(
          child: Column(
            children: [
              HbaTextField(label: 'Prix de vente (FCFA)', hint: 'Ex: 165000'),
              SizedBox(height: 14),
              HbaTextField(label: 'Prix barré (optionnel)', hint: 'Ex: 185000'),
              SizedBox(height: 14),
              HbaTextField(
                label: "Coût d'achat (optionnel)",
                hint: 'Ex: 120000',
              ),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              HbaTextField(label: 'Stock disponible', hint: 'Ex: 12'),
              SizedBox(height: 14),
              HbaTextField(label: 'Seuil stock faible', initial: '3'),
              SizedBox(height: 14),
              Text('Unité', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: 'pièce', selected: true),
                  GreenChoice(label: 'kg'),
                  GreenChoice(label: 'litre'),
                  GreenChoice(label: 'paquet'),
                  GreenChoice(label: 'portion'),
                ],
              ),
              SizedBox(height: 14),
              FormSwitchRow('Produit actif', selected: true),
              FormSwitchRow('Suivre le stock', selected: true),
            ],
          ),
        ),
        SizedBox(height: 14),
        ProductCalculatedSummary(),
      ],
    );
  }
}

class DynamicAttributesStep extends StatelessWidget {
  const DynamicAttributesStep({super.key, required this.category});

  final ProductCategory category;

  @override
  Widget build(BuildContext context) {
    final fields = switch (category) {
      ProductCategory.phones => [
        'Système',
        'Stockage',
        'RAM',
        'Réseau',
        'Double SIM',
        'Garantie',
      ],
      ProductCategory.audio => [
        'Type audio',
        'Bluetooth',
        'Autonomie',
        'Réduction de bruit',
        'Micro intégré',
      ],
      ProductCategory.accessories => [
        'Type accessoire',
        'Compatibilité',
        'Matière',
        'Couleur',
      ],
      ProductCategory.food => [
        'Poids / volume',
        'Date expiration',
        'Origine',
        'Conservation',
        'Allergènes',
      ],
      ProductCategory.fashion => [
        'Tailles',
        'Couleurs',
        'Matière',
        'Genre',
        'Retours acceptés',
      ],
      ProductCategory.beauty => [
        'Type de peau',
        'Volume',
        'Ingrédients',
        'Expiration',
        'Précautions',
      ],
      ProductCategory.restaurantDish => [
        'Temps préparation',
        'Type plat',
        'Allergènes',
        'Extras',
        'Taille portion',
      ],
      ProductCategory.appliances => [
        'Puissance',
        'Marque',
        'Modèle',
        'Garantie',
        'Classe énergétique',
      ],
    };
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Attributs requis', style: _titleStyle),
          const SizedBox(height: 6),
          const Text(
            'Le formulaire s’adapte à la catégorie choisie.',
            style: TextStyle(color: _muted),
          ),
          const SizedBox(height: 16),
          ...fields.map(
            (field) => Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: HbaTextField(label: field, initial: sampleValue(field)),
            ),
          ),
        ],
      ),
    );
  }
}

class DishDetailScreen extends StatelessWidget {
  const DishDetailScreen({super.key, required this.dish});

  final Product dish;

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Détail plat',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          ProductHero(product: dish),
          const SizedBox(height: 16),
          const InfoCard(
            title: 'Options client',
            lines: [
              'Niveau piment : doux, moyen, fort',
              'Portion : normal, grand',
              'Extras : alloco, sauce, boisson',
            ],
          ),
          const SizedBox(height: 16),
          const InfoCard(
            title: 'Cuisine',
            lines: [
              'Temps préparation : 20 min',
              'Station : chaud',
              'Instructions : emballage séparé pour la sauce',
            ],
          ),
          const SizedBox(height: 16),
          HbaButton(
            label: 'Modifier le plat',
            color: _orange,
            onPressed: () => pushPage(context, DishFormScreen(dish: dish)),
          ),
        ],
      ),
    );
  }
}

class DishFormScreen extends StatefulWidget {
  const DishFormScreen({super.key, this.dish});

  final Product? dish;

  @override
  State<DishFormScreen> createState() => _DishFormScreenState();
}

class _DishFormScreenState extends State<DishFormScreen> {
  int step = 0;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _bg,
      body: SafeArea(
        child: Column(
          children: [
            FormTopBar(
              title: widget.dish == null ? 'Nouveau plat' : 'Modifier le plat',
            ),
            RestaurantFormProgress(step: step),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
                children: [
                  if (step == 0) const DishInfoStep(),
                  if (step == 1) const DishPriceStep(),
                  if (step == 2) const DishPrepStep(),
                  if (step == 3) const DishOptionsStep(),
                  if (step == 4) const DishPublishStep(),
                ],
              ),
            ),
            DishFormFooter(
              step: step,
              onBack: () => setState(() => step -= 1),
              onNext: () {
                if (step < 4) {
                  setState(() => step += 1);
                  return;
                }
                _confirmSave();
              },
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _confirmSave() async {
    final saved = await showDialog<bool>(
      context: context,
      barrierColor: Colors.black.withValues(alpha: 0.55),
      builder: (context) => const SaveDishDialog(),
    );
    if (!mounted || saved != true) return;
    Navigator.pop(context);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        behavior: SnackBarBehavior.floating,
        margin: const EdgeInsets.fromLTRB(20, 12, 20, 720),
        backgroundColor: _green,
        content: Row(
          children: const [
            Icon(Icons.check_rounded, color: Colors.white),
            SizedBox(width: 10),
            Expanded(child: Text('Plat mis à jour ✓')),
          ],
        ),
      ),
    );
  }
}

class FormTopBar extends StatelessWidget {
  const FormTopBar({super.key, required this.title});

  final String title;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(20, 14, 20, 14),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(bottom: BorderSide(color: _border)),
      ),
      child: Row(
        children: [
          IconButton.outlined(
            style: IconButton.styleFrom(
              backgroundColor: const Color(0xFFF8FAFC),
              side: const BorderSide(color: _border),
            ),
            onPressed: () => Navigator.pop(context),
            icon: const Icon(Icons.chevron_left_rounded),
          ),
          const SizedBox(width: 10),
          Text(
            title,
            style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class RestaurantFormProgress extends StatelessWidget {
  const RestaurantFormProgress({super.key, required this.step});

  final int step;

  static const labels = ['Infos', 'Prix', 'Prépa.', 'Options', 'Pub.'];

  @override
  Widget build(BuildContext context) {
    return Container(
      color: Colors.white,
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 14),
      child: Column(
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Étape ${step + 1}/5 — ${labels[step]}',
                  style: const TextStyle(color: _muted, fontSize: 13),
                ),
              ),
              Text(
                '${(step + 1) * 20}%',
                style: const TextStyle(color: Color(0xFF98A2B3), fontSize: 13),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              for (var i = 0; i < labels.length; i++) ...[
                Expanded(
                  child: Column(
                    children: [
                      Container(
                        height: 6,
                        decoration: BoxDecoration(
                          color: i <= step
                              ? const Color(0xFFFF5A00)
                              : const Color(0xFFE4E7EC),
                          borderRadius: BorderRadius.circular(99),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        labels[i],
                        style: TextStyle(
                          color: i <= step
                              ? const Color(0xFFFF5A00)
                              : const Color(0xFF98A2B3),
                          fontSize: 10,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ],
                  ),
                ),
                if (i < labels.length - 1) const SizedBox(width: 7),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class ProductFormProgress extends StatelessWidget {
  const ProductFormProgress({super.key, required this.step});

  final int step;

  static const labels = ['Infos', 'Prix', 'Photos', 'Attributs', 'Livraison'];

  @override
  Widget build(BuildContext context) {
    return Container(
      color: Colors.white,
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 14),
      child: Column(
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Étape ${step + 1} sur 5 — ${labels[step]}',
                  style: const TextStyle(color: _muted, fontSize: 13),
                ),
              ),
              Text(
                '${(step + 1) * 20}%',
                style: const TextStyle(color: Color(0xFF98A2B3), fontSize: 13),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              for (var i = 0; i < labels.length; i++) ...[
                Expanded(
                  child: Column(
                    children: [
                      Container(
                        height: 6,
                        decoration: BoxDecoration(
                          color: i <= step ? _green : const Color(0xFFE4E7EC),
                          borderRadius: BorderRadius.circular(99),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        labels[i],
                        style: TextStyle(
                          color: i <= step ? _green : const Color(0xFF98A2B3),
                          fontSize: 10,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ],
                  ),
                ),
                if (i < labels.length - 1) const SizedBox(width: 7),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class ProductPhotosStep extends StatelessWidget {
  const ProductPhotosStep({super.key, this.filled = false});

  final bool filled;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Container(
          width: double.infinity,
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: const Color(0xFFEFF6FF),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: const Color(0xFFBFDBFE)),
          ),
          child: const Row(
            children: [
              Icon(Icons.info_outline_rounded, color: _blue, size: 16),
              SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Ajoutez des photos nettes sur fond clair pour de meilleures ventes.',
                  style: TextStyle(color: Color(0xFF1D4ED8), fontSize: 13),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        GridView.count(
          crossAxisCount: 3,
          mainAxisSpacing: 10,
          crossAxisSpacing: 10,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          children: List.generate(
            6,
            (index) => ProductPhotoSlot(index: index, filled: filled),
          ),
        ),
        const SizedBox(height: 14),
        HbaCard(
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: const [
              Icon(Icons.refresh_rounded, color: _muted, size: 18),
              SizedBox(width: 8),
              Text(
                'Réorganiser les photos',
                style: TextStyle(
                  color: Color(0xFF475467),
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class ProductPhotoSlot extends StatelessWidget {
  const ProductPhotoSlot({
    super.key,
    required this.index,
    required this.filled,
  });

  final int index;
  final bool filled;

  @override
  Widget build(BuildContext context) {
    if (!filled) {
      return Container(
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: const Color(0xFFCBD5E1), width: 1.4),
        ),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.add_rounded, color: Color(0xFF98A2B3)),
              const SizedBox(height: 8),
              Text(
                index == 0 ? 'Principale' : 'Photo ${index + 1}',
                style: const TextStyle(color: Color(0xFF98A2B3), fontSize: 11),
              ),
            ],
          ),
        ),
      );
    }

    final colors = [
      _blue,
      const Color(0xFFFBBF24),
      const Color(0xFF92400E),
      _blue,
      const Color(0xFFFBBF24),
      const Color(0xFF92400E),
    ];
    return Stack(
      children: [
        Container(
          decoration: BoxDecoration(
            color: colors[index].withValues(alpha: 0.2),
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: _green, width: 1.3),
          ),
          child: Center(
            child: Icon(
              Icons.smartphone_rounded,
              color: colors[index],
              size: 34,
            ),
          ),
        ),
        if (index == 0)
          Positioned(
            left: 6,
            top: 6,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 3),
              decoration: BoxDecoration(
                color: _green,
                borderRadius: BorderRadius.circular(999),
              ),
              child: const Text(
                'Principale',
                style: TextStyle(
                  color: Colors.white,
                  fontSize: 9,
                  fontWeight: FontWeight.w900,
                ),
              ),
            ),
          ),
        Positioned(
          right: 6,
          top: 6,
          child: Container(
            width: 20,
            height: 20,
            decoration: const BoxDecoration(
              color: Colors.white,
              shape: BoxShape.circle,
            ),
            child: const Icon(Icons.close_rounded, color: _red, size: 14),
          ),
        ),
      ],
    );
  }
}

class ProductAttributesStep extends StatelessWidget {
  const ProductAttributesStep({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: const [
        InfoBanner('Attributs requis pour Téléphones'),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Système', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: 'Android', selected: true),
                  GreenChoice(label: 'iOS'),
                  GreenChoice(label: 'Autre'),
                ],
              ),
              SizedBox(height: 14),
              Text('Stockage', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: '64 Go', selected: true),
                  GreenChoice(label: '128 Go'),
                  GreenChoice(label: '256 Go'),
                  GreenChoice(label: '512 Go'),
                ],
              ),
              SizedBox(height: 14),
              Text('RAM', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: '4 Go', selected: true),
                  GreenChoice(label: '6 Go'),
                  GreenChoice(label: '8 Go'),
                  GreenChoice(label: '12 Go'),
                ],
              ),
              SizedBox(height: 14),
              Text('Réseau', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: '4G', selected: true),
                  GreenChoice(label: '5G'),
                ],
              ),
              SizedBox(height: 14),
              HbaTextField(label: 'Taille écran (pouces)', hint: 'Ex: 6.6'),
              SizedBox(height: 14),
              FormSwitchRow('Double SIM'),
              SizedBox(height: 10),
              HbaTextField(
                label: 'Couleurs disponibles',
                hint: 'Ex: Noir, Bleu, Violet',
              ),
              SizedBox(height: 14),
              HbaTextField(label: 'Garantie (mois)', hint: 'Ex: 12'),
            ],
          ),
        ),
      ],
    );
  }
}

class ProductDeliveryStep extends StatelessWidget {
  const ProductDeliveryStep({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: const [
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('LIVRAISON'),
              FormSwitchRow('Produit livrable', selected: true),
              FormSwitchRow('Retrait en boutique', selected: true),
              FormSwitchRow('Frais de livraison inclus'),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: HbaTextField(label: 'Poids (kg)', hint: 'Ex: 0.8'),
                  ),
                  SizedBox(width: 12),
                  Expanded(
                    child: HbaTextField(
                      label: 'Dimensions (cm)',
                      hint: 'Ex: 16×8×1',
                    ),
                  ),
                ],
              ),
              SizedBox(height: 14),
              Text('Zones de livraison', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  GreenChoice(label: 'Cotonou', soft: true),
                  GreenChoice(label: 'Akpakpa', soft: true),
                  GreenChoice(label: 'Calavi', soft: true),
                  GreenChoice(label: 'Godomey', soft: true),
                  GreenChoice(label: 'Porto-Novo', soft: true),
                  GreenChoice(label: '+'),
                ],
              ),
              SizedBox(height: 14),
              Text('Délai de préparation', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  GreenChoice(label: '2h'),
                  GreenChoice(label: '24h', selected: true),
                  GreenChoice(label: '48h'),
                  GreenChoice(label: '3–5 jours'),
                ],
              ),
              SizedBox(height: 14),
              Text('Politique de retour', style: _fieldLabelStyle),
              SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  GreenChoice(label: 'Aucun retour'),
                  GreenChoice(label: '7 jours', selected: true),
                  GreenChoice(label: '14 jours'),
                  GreenChoice(label: '30 jours'),
                ],
              ),
            ],
          ),
        ),
        SizedBox(height: 14),
        ProductPublicationChecklist(),
      ],
    );
  }
}

class DishInfoStep extends StatelessWidget {
  const DishInfoStep({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: const [
          HbaTextField(label: 'Nom du plat', initial: 'Riz au poulet braisé'),
          SizedBox(height: 14),
          Text('Catégorie', style: _fieldLabelStyle),
          SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              OrangeChoice(label: 'Entrée'),
              OrangeChoice(label: 'Plat', selected: true),
              OrangeChoice(label: 'Boisson'),
              OrangeChoice(label: 'Dessert'),
              OrangeChoice(label: 'Accompagnement'),
            ],
          ),
          SizedBox(height: 14),
          HbaTextField(
            label: 'Description courte',
            initial: 'Riz parfumé servi avec du poulet braisé aux',
          ),
          SizedBox(height: 14),
          HbaTextField(
            label: 'Description détaillée',
            initial: 'Riz parfumé servi avec du poulet braisé aux épices locales, sauce tomate maison.',
            maxLines: 3,
          ),
          SizedBox(height: 16),
          DishPhotoBox(),
        ],
      ),
    );
  }
}

class DishPriceStep extends StatelessWidget {
  const DishPriceStep({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: const [
        HbaCard(
          child: Column(
            children: [
              HbaTextField(label: 'Prix de vente (FCFA)', initial: '3500'),
              SizedBox(height: 14),
              HbaTextField(
                label: 'Prix promotionnel (optionnel)',
                hint: 'Ex: 2900',
              ),
              SizedBox(height: 14),
              HbaTextField(
                label: "Quantité disponible aujourd'hui",
                initial: '15',
              ),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('DISPONIBILITÉ HORAIRE'),
              FormRadioRow('Toute la journée', selected: true),
              FormRadioRow('Petit-déjeuner'),
              FormRadioRow('Déjeuner'),
              FormRadioRow('Dîner'),
              FormRadioRow('Plage personnalisée'),
            ],
          ),
        ),
      ],
    );
  }
}

class DishPrepStep extends StatelessWidget {
  const DishPrepStep({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: const [
          HbaTextField(label: 'Temps moyen préparation (min)', initial: '20'),
          SizedBox(height: 14),
          Text('Difficulté', style: _fieldLabelStyle),
          SizedBox(height: 8),
          Row(
            children: [
              Expanded(child: OrangeChoice(label: 'Facile')),
              SizedBox(width: 8),
              Expanded(child: OrangeChoice(label: 'Moyen', selected: true)),
              SizedBox(width: 8),
              Expanded(child: OrangeChoice(label: 'Difficile')),
            ],
          ),
          SizedBox(height: 14),
          Text('Station cuisine', style: _fieldLabelStyle),
          SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              OrangeChoice(label: 'Grill'),
              OrangeChoice(label: 'Chaud', selected: true),
              OrangeChoice(label: 'Froid'),
              OrangeChoice(label: 'Boisson'),
              OrangeChoice(label: 'Dessert'),
            ],
          ),
          SizedBox(height: 14),
          HbaTextField(
            label: 'Instructions internes cuisine',
            hint: "Instructions pour l'équipe cuisine...",
            maxLines: 4,
          ),
        ],
      ),
    );
  }
}

class DishOptionsStep extends StatelessWidget {
  const DishOptionsStep({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: const [
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('TAILLES DE PORTION'),
              Row(
                children: [
                  Expanded(
                    child: OrangeChoice(label: 'Normal', selected: true),
                  ),
                  SizedBox(width: 8),
                  Expanded(child: OrangeChoice(label: 'Grand', selected: true)),
                  SizedBox(width: 8),
                  Expanded(child: OrangeChoice(label: 'Familial')),
                ],
              ),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: SmallSectionTitle('EXTRAS')),
                  Text(
                    '+\nAjouter',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Color(0xFFFF5A00),
                      fontWeight: FontWeight.w800,
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
              ExtraRow('Alloco', '500'),
              SizedBox(height: 10),
              ExtraRow('Boisson', '500'),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('CHOIX CLIENT'),
              ChoiceSummary(title: 'Niveau piment', value: 'Doux, Moyen, Fort'),
              ChoiceSummary(title: 'Sauce', value: 'Tomate, Gombo, Piment'),
              ChoiceSummary(
                title: 'Accompagnement',
                value: 'Alloco, Riz, Salade',
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class DishPublishStep extends StatelessWidget {
  const DishPublishStep({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: const [
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('ALLERGÈNES'),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  OrangeChoice(label: 'Gluten', selected: true),
                  OrangeChoice(label: 'Lait'),
                  OrangeChoice(label: 'Œufs'),
                  OrangeChoice(label: 'Arachide'),
                  OrangeChoice(label: 'Poisson'),
                  OrangeChoice(label: 'Soja'),
                  OrangeChoice(label: 'Sésame'),
                ],
              ),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('RÉGIME ALIMENTAIRE'),
              FormSwitchRow('Halal'),
              FormSwitchRow('Végétarien'),
              FormSwitchRow('Végan'),
              FormSwitchRow('Sans gluten'),
            ],
          ),
        ),
        SizedBox(height: 14),
        HbaCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SmallSectionTitle('CHECKLIST PUBLICATION'),
              ChecklistRow('Nom renseigné'),
              ChecklistRow('Prix renseigné'),
              ChecklistRow('Temps préparation'),
              ChecklistRow('Options vérifiées'),
              ChecklistRow('Catégorie sélectionnée'),
            ],
          ),
        ),
      ],
    );
  }
}

class DishFormFooter extends StatelessWidget {
  const DishFormFooter({
    super.key,
    required this.step,
    required this.onBack,
    required this.onNext,
    this.color = const Color(0xFFFF5A00),
    this.finalLabel = 'Enregistrer',
  });

  final int step;
  final VoidCallback onBack;
  final VoidCallback onNext;
  final Color color;
  final String finalLabel;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(
        20,
        12,
        20,
        MediaQuery.of(context).padding.bottom + 12,
      ),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: _border)),
      ),
      child: Row(
        children: [
          Expanded(
            child: HbaButton(
              label: step == 0 ? 'Annuler' : '← Retour',
              outlined: true,
              onPressed: step == 0 ? () => Navigator.pop(context) : onBack,
            ),
          ),
          const SizedBox(width: 12),
          if (step == 4) ...[
            Expanded(
              child: HbaButton(
                label: 'Brouillon',
                outlined: true,
                onPressed: () {},
              ),
            ),
            const SizedBox(width: 12),
          ],
          Expanded(
            flex: step == 4 ? 1 : 2,
            child: HbaButton(
              label: step == 4 ? finalLabel : 'Continuer →',
              color: color,
              onPressed: onNext,
            ),
          ),
        ],
      ),
    );
  }
}

class SaveDishDialog extends StatelessWidget {
  const SaveDishDialog({super.key});

  @override
  Widget build(BuildContext context) {
    return Dialog(
      insetPadding: const EdgeInsets.symmetric(horizontal: 44),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: const [
                IconBox(icon: Icons.check_rounded, color: _green, size: 52),
                SizedBox(height: 24),
                Text(
                  'Enregistrer les modifications ?',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
                ),
                SizedBox(height: 12),
                Text(
                  'Les modifications seront visibles immédiatement.',
                  style: TextStyle(color: _muted, height: 1.4),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          Row(
            children: [
              Expanded(
                child: TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const Text('Annuler'),
                ),
              ),
              Container(width: 1, height: 52, color: _border),
              Expanded(
                child: TextButton(
                  onPressed: () => Navigator.pop(context, true),
                  child: const Text(
                    'Enregistrer',
                    style: TextStyle(
                      color: _green,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class OrderDetailScreen extends StatelessWidget {
  const OrderDetailScreen({
    super.key,
    required this.order,
    required this.restaurant,
  });

  final Order order;
  final bool restaurant;

  @override
  Widget build(BuildContext context) {
    final progress = order.id == 'CMD-2845'
        ? 3
        : order.id == 'CMD-2842'
        ? 0
        : 1;
    final cancelled = order.id == 'CMD-2842';
    return Scaffold(
      backgroundColor: _bg,
      body: SafeArea(
        child: Column(
          children: [
            const FormTopBar(title: 'Détail commande'),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
                children: [
                  HbaCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                order.id,
                                style: const TextStyle(
                                  fontSize: 20,
                                  fontWeight: FontWeight.w900,
                                ),
                              ),
                            ),
                            StatusBadge(
                              cancelled ? 'Annulée' : order.status,
                              color: cancelled
                                  ? _red
                                  : (order.status == 'Préparation'
                                        ? _orange
                                        : _blue),
                              compact: true,
                            ),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Cotonou · Il y a ${order.time}',
                          style: const TextStyle(color: _muted),
                        ),
                        if (order.urgent) ...[
                          const SizedBox(height: 14),
                          Container(
                            width: double.infinity,
                            padding: const EdgeInsets.all(12),
                            decoration: BoxDecoration(
                              color: const Color(0xFFFEF2F2),
                              borderRadius: BorderRadius.circular(12),
                            ),
                            child: const Row(
                              children: [
                                Icon(
                                  Icons.warning_amber_rounded,
                                  color: _red,
                                  size: 18,
                                ),
                                SizedBox(width: 8),
                                Text(
                                  'Commande urgente',
                                  style: TextStyle(
                                    color: _red,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: 14),
                  OrderProgressCard(progress: progress),
                  const SizedBox(height: 14),
                  OrderClientCard(order: order, cancelled: cancelled),
                  const SizedBox(height: 14),
                  OrderArticlesCard(count: order.items, total: order.amount),
                ],
              ),
            ),
            OrderDetailFooter(
              order: order,
              progress: progress,
              cancelled: cancelled,
            ),
          ],
        ),
      ),
    );
  }
}

class OrderProgressCard extends StatelessWidget {
  const OrderProgressCard({super.key, required this.progress});

  final int progress;

  @override
  Widget build(BuildContext context) {
    const steps = ['Reçue', 'Acceptée', 'En préparation', 'Prête', 'Livrée'];
    const times = ['10:32', '10:35', '10:38', '', ''];
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SmallSectionTitle('PROGRESSION'),
          for (var i = 0; i < steps.length; i++)
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Column(
                  children: [
                    Container(
                      width: 24,
                      height: 24,
                      decoration: BoxDecoration(
                        color: i < progress
                            ? const Color(0xFF10B981)
                            : Colors.white,
                        shape: BoxShape.circle,
                        border: Border.all(
                          color: i < progress
                              ? const Color(0xFF10B981)
                              : const Color(0xFFE4E7EC),
                          width: 2,
                        ),
                      ),
                      child: i < progress
                          ? const Icon(
                              Icons.check_rounded,
                              color: Colors.white,
                              size: 15,
                            )
                          : null,
                    ),
                    if (i < steps.length - 1)
                      Container(
                        width: 2,
                        height: 30,
                        color: i < progress - 1
                            ? const Color(0xFF10B981)
                            : const Color(0xFFE4E7EC),
                      ),
                  ],
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          steps[i],
                          style: TextStyle(
                            color: i < progress
                                ? _text
                                : const Color(0xFF98A2B3),
                            fontWeight: i == progress - 1
                                ? FontWeight.w900
                                : FontWeight.w700,
                            fontSize: 16,
                          ),
                        ),
                        if (times[i].isNotEmpty)
                          Text(
                            times[i],
                            style: const TextStyle(
                              color: Color(0xFF98A2B3),
                              fontSize: 12,
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class OrderClientCard extends StatelessWidget {
  const OrderClientCard({
    super.key,
    required this.order,
    required this.cancelled,
  });

  final Order order;
  final bool cancelled;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SmallSectionTitle('CLIENT'),
          Row(
            children: [
              const IconBox(
                icon: Icons.person_outline_rounded,
                color: _muted,
                size: 40,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      order.client,
                      style: const TextStyle(fontWeight: FontWeight.w900),
                    ),
                    const Text(
                      '+229 97 45 12 38',
                      style: TextStyle(color: _muted, fontSize: 12),
                    ),
                  ],
                ),
              ),
              const IconBox(
                icon: Icons.phone_outlined,
                color: _green,
                size: 38,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              const Icon(
                Icons.location_on_outlined,
                size: 15,
                color: Color(0xFF98A2B3),
              ),
              const SizedBox(width: 5),
              Expanded(
                child: Text(
                  order.address,
                  style: const TextStyle(color: _muted, fontSize: 13),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(10),
            decoration: BoxDecoration(
              color: const Color(0xFFFFF7ED),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Text(
              cancelled
                  ? 'Annulée par le client'
                  : (order.id == 'CMD-2845'
                        ? 'Bien cuire le poisson'
                        : 'Sans oignon svp'),
              style: const TextStyle(color: Color(0xFFEA580C), fontSize: 13),
            ),
          ),
        ],
      ),
    );
  }
}

class OrderArticlesCard extends StatelessWidget {
  const OrderArticlesCard({
    super.key,
    required this.count,
    required this.total,
  });

  final int count;
  final String total;

  @override
  Widget build(BuildContext context) {
    const items = [
      ('Samsung Galaxy A35', '165 000 F'),
      ('Coque protection', '3 500 F'),
      ('Chargeur rapide', '9 500 F'),
    ];
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SmallSectionTitle('ARTICLES ($count)'),
          for (var i = 0; i < items.length && i < count; i++)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 9),
              child: Row(
                children: [
                  Container(
                    width: 28,
                    height: 28,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: _bg,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text(
                      '${i + 1}',
                      style: const TextStyle(
                        color: _muted,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      items[i].$1,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                  Text(
                    items[i].$2,
                    style: const TextStyle(fontWeight: FontWeight.w900),
                  ),
                ],
              ),
            ),
          const Divider(),
          SummaryLine(label: 'Total', value: total),
        ],
      ),
    );
  }
}

class OrderDetailFooter extends StatelessWidget {
  const OrderDetailFooter({
    super.key,
    required this.order,
    required this.progress,
    required this.cancelled,
  });

  final Order order;
  final int progress;
  final bool cancelled;

  @override
  Widget build(BuildContext context) {
    if (cancelled) {
      return Container(
        padding: EdgeInsets.fromLTRB(
          20,
          12,
          20,
          MediaQuery.of(context).padding.bottom + 12,
        ),
        color: Colors.white,
        child: HbaButton(
          label: '×  Commande annulée',
          outlined: true,
          color: _red,
          onPressed: () {},
        ),
      );
    }
    if (progress >= 3) {
      return Container(
        padding: EdgeInsets.fromLTRB(
          20,
          12,
          20,
          MediaQuery.of(context).padding.bottom + 12,
        ),
        color: Colors.white,
        child: HbaButton(
          label: '✓  Marquer comme prête',
          color: _blue,
          onPressed: () {},
        ),
      );
    }
    return Container(
      padding: EdgeInsets.fromLTRB(
        20,
        12,
        20,
        MediaQuery.of(context).padding.bottom + 12,
      ),
      color: Colors.white,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          HbaButton(label: '✓  Accepter la commande', onPressed: () {}),
          const SizedBox(height: 10),
          HbaButton(
            label: 'Refuser',
            outlined: true,
            color: _red,
            onPressed: () {},
          ),
        ],
      ),
    );
  }
}

class MessagesScreen extends StatelessWidget {
  const MessagesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Messages',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          const SearchAndFilter(hint: 'Rechercher un client...'),
          const SizedBox(height: 12),
          const FilterChips(
            labels: ['Tous', 'Non lus', 'Commandes', 'Support'],
          ),
          const SizedBox(height: 16),
          ...conversations.map(
            (conversation) => ConversationTile(conversation: conversation),
          ),
        ],
      ),
    );
  }
}

class ConversationScreen extends StatelessWidget {
  const ConversationScreen({super.key, required this.conversation});

  final Conversation conversation;

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: conversation.name,
      actionIcon: Icons.info_outline,
      onAction: () => pushPage(context, const ClientInfoScreen()),
      child: Column(
        children: [
          if (conversation.orderId != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 0),
              child: HbaCard(
                child: Row(
                  children: [
                    const IconBox(icon: Icons.receipt_long, color: _blue),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        '${conversation.orderId} · En livraison',
                        style: const TextStyle(fontWeight: FontWeight.w800),
                      ),
                    ),
                    const Icon(Icons.chevron_right_rounded),
                  ],
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 12),
              children: const [
                MessageBubble(
                  text: 'Bonjour, le Samsung Galaxy A35 est-il toujours disponible ?',
                  incoming: true,
                  time: '09:41',
                ),
                MessageBubble(
                  text: 'Bonjour Fatou, oui il est disponible en bleu et noir.',
                  incoming: false,
                  time: '09:42',
                ),
                MessageBubble(
                  text: 'Est-ce possible de le livrer aujourd’hui à Calavi ?',
                  incoming: true,
                  time: '09:43',
                ),
                MessageBubble(
                  text: 'Oui, la livraison est possible aujourd’hui.',
                  incoming: false,
                  time: '09:44',
                ),
              ],
            ),
          ),
          const ChatInputBar(),
        ],
      ),
    );
  }
}

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Paramètres',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          HbaCard(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                MenuRow(
                  Icons.palette_outlined,
                  _green,
                  'Apparence',
                  'Thème, affichage',
                  () => pushPage(
                    context,
                    const PlaceholderDetail(title: 'Apparence'),
                  ),
                ),
                MenuRow(
                  Icons.credit_card_outlined,
                  _blue,
                  'Moyens de paiement',
                  'Mobile Money, banque',
                  () => pushPage(context, const PaymentMethodsScreen()),
                ),
                MenuRow(
                  Icons.description_outlined,
                  _orange,
                  'Factures & relevés',
                  'Exports, versements',
                  () => pushPage(context, const InvoicesScreen()),
                ),
                MenuRow(
                  Icons.lock_outline,
                  _green,
                  'Sécurité',
                  'Mot de passe, 2FA',
                  () => pushPage(
                    context,
                    const PlaceholderDetail(title: 'Sécurité'),
                  ),
                ),
                MenuRow(
                  Icons.privacy_tip_outlined,
                  Colors.purple,
                  'Confidentialité',
                  'Données et consentements',
                  () => pushPage(
                    context,
                    const PlaceholderDetail(title: 'Confidentialité'),
                  ),
                ),
                MenuRow(
                  Icons.gavel_outlined,
                  _muted,
                  'Conditions générales',
                  'Documents légaux',
                  () => pushPage(
                    context,
                    const PlaceholderDetail(title: 'Conditions générales'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class MyShopsScreen extends StatelessWidget {
  const MyShopsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Mes boutiques',
      actionIcon: Icons.add_rounded,
      onAction: () => pushPage(context, const AddShopScreen()),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: shops.map((shop) => ShopManageCard(shop: shop)).toList(),
      ),
    );
  }
}

class AddShopScreen extends StatefulWidget {
  const AddShopScreen({super.key});

  @override
  State<AddShopScreen> createState() => _AddShopScreenState();
}

class _AddShopScreenState extends State<AddShopScreen> {
  int step = 0;
  var type = ShopType.marketplace;

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Ajouter boutique',
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 12),
            child: StepHeader(
              step: step,
              total: 4,
              labels: const ['Type', 'Infos', 'Adresse', 'Valider'],
            ),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
              children: [
                if (step == 0)
                  HbaCard(
                    child: Column(
                      children: [
                        ChoiceListTile(
                          icon: Icons.storefront_rounded,
                          title: 'Marketplace',
                          selected: type == ShopType.marketplace,
                          onTap: () =>
                              setState(() => type = ShopType.marketplace),
                        ),
                        const SizedBox(height: 10),
                        ChoiceListTile(
                          icon: Icons.restaurant_rounded,
                          title: 'Restaurant',
                          selected: type == ShopType.restaurant,
                          onTap: () =>
                              setState(() => type = ShopType.restaurant),
                        ),
                      ],
                    ),
                  ),
                if (step == 1)
                  FormFieldsCard(
                    fields: type == ShopType.restaurant
                        ? const [
                            'Nom restaurant',
                            'Type cuisine',
                            'Téléphone',
                            'Temps moyen préparation',
                          ]
                        : const [
                            'Nom boutique',
                            'Catégorie principale',
                            'Téléphone',
                            'Responsable',
                          ],
                  ),
                if (step == 2)
                  const FormFieldsCard(
                    fields: [
                      'Adresse',
                      'Ville',
                      'Horaires',
                      'Zones desservies',
                    ],
                  ),
                if (step == 3) const PublishStep(title: 'Validation boutique'),
              ],
            ),
          ),
          FormFooter(
            canGoBack: step > 0,
            primaryLabel: step == 3 ? 'Créer la boutique' : 'Continuer',
            secondaryLabel: 'Retour',
            onBack: () => setState(() => step -= 1),
            onPrimary: () {
              if (step < 3) {
                setState(() => step += 1);
              } else {
                Navigator.pop(context);
              }
            },
          ),
        ],
      ),
    );
  }
}

class KitchenScreen extends StatelessWidget {
  const KitchenScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Cuisine',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          const FilterChips(labels: ['À préparer', 'En cours', 'Prêtes']),
          const SizedBox(height: 16),
          ...orders.map(
            (order) => HbaCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          order.id,
                          style: const TextStyle(
                            fontSize: 20,
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                      ),
                      StatusBadge(
                        order.urgent ? 'Urgent' : order.time,
                        color: order.urgent ? _red : _green,
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  const Text(
                    'Riz au poulet braisé x2\nJus de bissap x1',
                    style: TextStyle(height: 1.5),
                  ),
                  const SizedBox(height: 12),
                  HbaButton(
                    label: 'Marquer prête',
                    color: _orange,
                    onPressed: () {},
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class PaymentMethodsScreen extends StatelessWidget {
  const PaymentMethodsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Moyens de paiement',
      actionIcon: Icons.add_rounded,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: const [
          PaymentTile(
            'MTN Mobile Money',
            '+229 97 00 00 00',
            'Principal',
            _green,
          ),
          PaymentTile('Moov Money', '+229 96 00 00 00', 'Vérifié', _blue),
          PaymentTile('BOA Bénin', 'Compte **** 4582', 'En attente', _orange),
        ],
      ),
    );
  }
}

class InvoicesScreen extends StatelessWidget {
  const InvoicesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Factures & relevés',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: const [
          InfoCard(
            title: 'Résumé',
            lines: [
              'Solde disponible : 426 800 FCFA',
              'Revenus du mois : 1 820 000 FCFA',
              'Dernier versement : 50 000 FCFA',
            ],
          ),
          SizedBox(height: 16),
          InvoiceTile(
            'Facture #HBA-2026-001',
            'Août 2026',
            'Payée',
            '84 300 FCFA',
          ),
          InvoiceTile(
            'Relevé juillet 2026',
            'Juillet 2026',
            'Disponible',
            '1 640 000 FCFA',
          ),
          InvoiceTile(
            'Versement #PAY-8871',
            '26/08/2026',
            'Terminé',
            '250 000 FCFA',
          ),
        ],
      ),
    );
  }
}

class PlaceholderDetail extends StatelessWidget {
  const PlaceholderDetail({super.key, required this.title});

  final String title;

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: title,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
        children: [
          InfoCard(
            title: title,
            lines: const [
              'Écran prêt pour la connexion API.',
              'Les composants visuels sont alignés avec le prototype vendeur.',
            ],
          ),
        ],
      ),
    );
  }
}

class ReviewsScreen extends StatelessWidget {
  const ReviewsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Avis et notes',
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
        children: const [
          RatingSummaryCard(),
          SizedBox(height: 10),
          StateChipsStatic(label: 'Vider'),
          SizedBox(height: 8),
          FilterChips(labels: ['Tous', '5 ★', '4 ★', 'À répondre']),
          SizedBox(height: 14),
          ReviewCard(
            initial: 'K',
            name: 'Kofi Mensah',
            date: "Aujourd'hui",
            text: 'Livraison ultra rapide ! Produit exactement comme décrit. Je recommande vivement Awa Électronique.',
            product: 'Samsung Galaxy A35',
          ),
          ReviewCard(
            initial: 'F',
            name: 'Fatou Diallo',
            date: 'Hier',
            text: 'Bon produit, emballage soigné. Petit retard de 30 min sur la livraison mais le livreur était sympa.',
            product: 'Huile palme 5L',
            replied: true,
          ),
          ReviewCard(
            initial: 'R',
            name: 'Rachid Alabi',
            date: '02/09',
            text: 'Le chargeur ne charge pas aussi vite que prévu. Déçu par rapport à la description.',
            product: 'Chargeur rapide 65W',
          ),
          ReviewCard(
            initial: 'S',
            name: 'Sêdami Hounsinou',
            date: '01/09',
            text: 'Parfait ! Qualité premium au prix juste. Boutique sérieuse, commande bien préparée.',
            product: 'Écouteurs JBL Tune 230',
            replied: true,
          ),
          ReviewCard(
            initial: 'A',
            name: 'Aïcha Traoré',
            date: '31/08',
            text: "Correct mais le riz avait une odeur légèrement différente de l'habituel. Espère mieux la prochaine fois.",
            product: 'Riz local 25 kg',
          ),
        ],
      ),
    );
  }
}

class DeliveriesScreen extends StatefulWidget {
  const DeliveriesScreen({super.key});

  @override
  State<DeliveriesScreen> createState() => _DeliveriesScreenState();
}

class _DeliveriesScreenState extends State<DeliveriesScreen> {
  int tab = 0;

  @override
  Widget build(BuildContext context) {
    final lists = [
      const [
        DeliveryCard(
          order: 'CMD-2844',
          status: 'Assignée',
          statusColor: _blue,
          client: 'Rachid Alabi',
          address: 'Akpakpa centre, Cotonou',
          driver: 'Brice Agossa',
          eta: '14 min',
          id: 'LIV-441',
        ),
        DeliveryCard(
          order: 'CMD-2841',
          status: 'En route',
          statusColor: Colors.purple,
          client: 'Paul Atchadé',
          address: 'Porto-Novo centre',
          driver: 'Gildas Zinsou',
          eta: '28 min',
          id: 'LIV-439',
        ),
      ],
      const [
        DeliveryCard(
          order: 'CMD-2843',
          status: 'Livrée',
          statusColor: _green,
          client: 'Fatou Diallo',
          address: 'Calavi Gbèdji, Abomey-Calavi',
          driver: 'Moussa Kéita',
          eta: '',
          id: 'LIV-440',
        ),
      ],
      const [
        DeliveryCard(
          order: 'CMD-2839',
          status: 'Annulée',
          statusColor: _red,
          client: 'Ama Sossa',
          address: 'Cotonou Fidjrossè',
          driver: '',
          eta: '',
          id: 'LIV-438',
          showDriver: false,
        ),
      ],
    ];
    return DetailPage(
      title: 'Livraisons',
      child: Column(
        children: [
          SegmentedHeader(
            labels: const ['En cours', 'Terminées', 'Annulées'],
            selected: tab,
            onChanged: (value) => setState(() => tab = value),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
              children: [
                const Align(
                  alignment: Alignment.centerRight,
                  child: StateChipsStatic(label: 'Vider'),
                ),
                const SizedBox(height: 10),
                ...lists[tab],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class MembersScreen extends StatelessWidget {
  const MembersScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Membres & rôles',
      child: Column(
        children: [
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
              children: const [
                MemberCard(
                  initial: 'AK',
                  name: 'Amidou Kossou',
                  contact: 'amidou@awaelectronique.bj',
                  role: 'Propriétaire',
                  color: _green,
                ),
                MemberCard(
                  initial: 'CD',
                  name: 'Clémentine Dossou',
                  contact: 'clem@awaelectronique.bj',
                  role: 'Manager',
                  color: _blue,
                ),
                MemberCard(
                  initial: 'TA',
                  name: 'Théodore Agbodji',
                  contact: '+229 96 78 34 12',
                  role: 'Préparateur',
                  color: _orange,
                ),
                MemberCard(
                  initial: 'BH',
                  name: 'Bénédicte Hounton',
                  contact: 'bene@awaelectronique.bj',
                  role: 'Finance',
                  color: Colors.purple,
                  inactive: true,
                ),
                PermissionsCard(),
              ],
            ),
          ),
          Container(
            padding: EdgeInsets.fromLTRB(
              20,
              12,
              20,
              MediaQuery.of(context).padding.bottom + 12,
            ),
            color: Colors.white,
            child: HbaButton(
              label: 'Inviter un membre',
              onPressed: () => pushPage(context, const InviteMemberScreen()),
            ),
          ),
        ],
      ),
    );
  }
}

class InviteMemberScreen extends StatelessWidget {
  const InviteMemberScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DetailPage(
      title: 'Inviter un membre',
      child: Column(
        children: [
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
              children: const [
                HbaCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SmallSectionTitle('INFORMATIONS DU MEMBRE'),
                      HbaTextField(
                        label: 'Nom complet',
                        hint: 'Ex: Clémentine Dossou',
                      ),
                      SizedBox(height: 14),
                      HbaTextField(
                        label: 'Email ou téléphone',
                        hint: 'email@exemple.bj ou +229...',
                      ),
                      SizedBox(height: 14),
                      Text('Rôle', style: _fieldLabelStyle),
                      SizedBox(height: 8),
                      Row(
                        children: [
                          Expanded(
                            child: GreenChoice(
                              label: 'Manager',
                              selected: true,
                            ),
                          ),
                          SizedBox(width: 8),
                          Expanded(child: GreenChoice(label: 'Préparateur')),
                        ],
                      ),
                      SizedBox(height: 8),
                      Row(
                        children: [
                          Expanded(child: GreenChoice(label: 'Finance')),
                          SizedBox(width: 8),
                          Expanded(child: GreenChoice(label: 'Support')),
                        ],
                      ),
                      SizedBox(height: 14),
                      HbaTextField(
                        label: 'Boutique associée',
                        initial: 'Awa Électronique',
                      ),
                    ],
                  ),
                ),
                SizedBox(height: 14),
                HbaCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SmallSectionTitle('PERMISSIONS'),
                      PermissionCheckRow('Gérer les produits', selected: true),
                      PermissionCheckRow('Gérer les commandes', selected: true),
                      PermissionCheckRow('Gérer les paiements'),
                      PermissionCheckRow(
                        'Voir les statistiques',
                        selected: true,
                      ),
                      PermissionCheckRow('Gérer les promotions'),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Container(
            padding: EdgeInsets.fromLTRB(
              20,
              12,
              20,
              MediaQuery.of(context).padding.bottom + 12,
            ),
            color: Colors.white,
            child: HbaButton(
              label: "Envoyer l'invitation",
              onPressed: () => Navigator.pop(context),
            ),
          ),
        ],
      ),
    );
  }
}

class NotificationsScreen extends StatelessWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const PlaceholderDetail(title: 'Notifications');
}

class SupportScreen extends StatelessWidget {
  const SupportScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const PlaceholderDetail(title: 'Support');
}

class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const PlaceholderDetail(title: 'Profil');
}

class ClientInfoScreen extends StatelessWidget {
  const ClientInfoScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const PlaceholderDetail(title: 'Infos client');
}

class MediaStep extends StatelessWidget {
  const MediaStep({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            height: 170,
            decoration: BoxDecoration(
              color: _bg,
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: _border),
            ),
            child: const Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.cloud_upload_outlined, size: 36, color: _muted),
                  SizedBox(height: 8),
                  Text(
                    'Ajouter une photo principale',
                    style: TextStyle(fontWeight: FontWeight.w700),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 10,
            runSpacing: 10,
            children: List.generate(
              6,
              (index) => Container(
                width: 72,
                height: 72,
                decoration: BoxDecoration(
                  color: _bg,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: _border),
                ),
                child: Icon(
                  index == 0
                      ? Icons.check_circle
                      : Icons.add_photo_alternate_outlined,
                  color: index == 0 ? _green : _muted,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class PublishStep extends StatelessWidget {
  const PublishStep({
    super.key,
    this.foodMode = false,
    this.title = 'Publication',
  });

  final bool foodMode;
  final String title;

  @override
  Widget build(BuildContext context) {
    final items = foodMode
        ? [
            'Nom renseigné',
            'Prix renseigné',
            'Photo ajoutée',
            'Temps préparation renseigné',
            'Options vérifiées',
          ]
        : [
            'Informations complètes',
            'Prix renseigné',
            'Stock renseigné',
            'Photo principale ajoutée',
            'Attributs complétés',
          ];
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: _titleStyle),
          const SizedBox(height: 14),
          ...items.map(
            (item) => Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Row(
                children: [
                  const Icon(Icons.check_circle_rounded, color: _green),
                  const SizedBox(width: 10),
                  Text(
                    item,
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class FormFieldsCard extends StatelessWidget {
  const FormFieldsCard({super.key, required this.fields});

  final List<String> fields;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        children: fields
            .map(
              (field) => Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: HbaTextField(label: field, initial: sampleValue(field)),
              ),
            )
            .toList(),
      ),
    );
  }
}

class ShopSelectorSheet extends StatelessWidget {
  const ShopSelectorSheet({
    super.key,
    required this.activeShop,
    required this.onSelected,
  });

  final Shop activeShop;
  final ValueChanged<Shop> onSelected;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        16,
        20,
        MediaQuery.of(context).padding.bottom + 20,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Center(
            child: Container(
              width: 42,
              height: 4,
              decoration: BoxDecoration(
                color: _border,
                borderRadius: BorderRadius.circular(99),
              ),
            ),
          ),
          const SizedBox(height: 18),
          const Text(
            'Changer de boutique',
            style: TextStyle(fontSize: 22, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 14),
          const SearchAndFilter(
            hint: 'Rechercher une boutique...',
            filter: false,
          ),
          const SizedBox(height: 14),
          ...shops.map(
            (shop) => HbaCard(
              margin: const EdgeInsets.only(bottom: 10),
              child: TransparentMaterial(
                child: ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: IconBox(
                    icon: shop.type == ShopType.restaurant
                        ? Icons.restaurant_rounded
                        : Icons.storefront_rounded,
                    color: shop.type == ShopType.restaurant ? _orange : _green,
                  ),
                  title: Text(
                    shop.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontWeight: FontWeight.w800),
                  ),
                  subtitle: Text(
                    '${shop.typeLabel} · ${shop.address} · ${shop.statusLabel}',
                  ),
                  trailing: activeShop.name == shop.name
                      ? const Icon(Icons.check_circle_rounded, color: _green)
                      : const Icon(Icons.chevron_right_rounded),
                  onTap: () => onSelected(shop),
                ),
              ),
            ),
          ),
          const SizedBox(height: 8),
          HbaButton(
            label: 'Gérer les boutiques',
            outlined: true,
            onPressed: () => pushPage(context, const MyShopsScreen()),
          ),
        ],
      ),
    );
  }
}

class HbaPage extends StatelessWidget {
  const HbaPage({
    super.key,
    required this.title,
    required this.child,
    this.subtitle,
    this.trailing,
  });

  final String title;
  final String? subtitle;
  final Widget child;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 18, 16, 10),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      style: const TextStyle(
                        fontSize: 28,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                    if (subtitle case final value?)
                      Text(value, style: const TextStyle(color: _muted)),
                  ],
                ),
              ),
              ?trailing,
            ],
          ),
        ),
        Expanded(child: child),
      ],
    );
  }
}

class DetailPage extends StatelessWidget {
  const DetailPage({
    super.key,
    required this.title,
    required this.child,
    this.actionIcon,
    this.onAction,
  });

  final String title;
  final Widget child;
  final IconData? actionIcon;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _bg,
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(8, 12, 12, 8),
              child: Row(
                children: [
                  IconButton(
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.chevron_left_rounded),
                  ),
                  Expanded(
                    child: Text(
                      title,
                      style: const TextStyle(
                        fontSize: 23,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                  ),
                  if (actionIcon != null)
                    IconButton(onPressed: onAction, icon: Icon(actionIcon)),
                ],
              ),
            ),
            Expanded(child: child),
          ],
        ),
      ),
    );
  }
}

class HbaCard extends StatelessWidget {
  const HbaCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.margin = const EdgeInsets.only(bottom: 12),
    this.color,
  });

  final Widget child;
  final EdgeInsets padding;
  final EdgeInsets margin;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: margin,
      padding: padding,
      decoration: BoxDecoration(
        color: color ?? Colors.white,
        borderRadius: BorderRadius.circular(18),
        border: color == null ? Border.all(color: _border) : null,
        boxShadow: color == null
            ? [
                BoxShadow(
                  color: Colors.black.withValues(alpha: 0.05),
                  blurRadius: 10,
                  offset: const Offset(0, 3),
                ),
              ]
            : null,
      ),
      child: child,
    );
  }
}

class TransparentMaterial extends StatelessWidget {
  const TransparentMaterial({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Material(type: MaterialType.transparency, child: child);
  }
}

class ShopChip extends StatelessWidget {
  const ShopChip({super.key, required this.shop, required this.onTap});

  final Shop shop;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(16),
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: _border),
        ),
        child: Row(
          children: [
            IconBox(
              icon: shop.type == ShopType.restaurant
                  ? Icons.restaurant_rounded
                  : Icons.storefront_rounded,
              color: shop.type == ShopType.restaurant ? _orange : _green,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    shop.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontWeight: FontWeight.w900),
                  ),
                  Text(
                    '${shop.address} · ${shop.statusLabel}',
                    style: TextStyle(
                      color: shop.statusColor,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
              ),
            ),
            const Icon(Icons.keyboard_arrow_down_rounded),
          ],
        ),
      ),
    );
  }
}

class ProductCard extends StatelessWidget {
  const ProductCard({
    super.key,
    required this.product,
    required this.onTap,
    required this.onEdit,
    this.foodMode = false,
  });

  final Product product;
  final VoidCallback onTap;
  final VoidCallback onEdit;
  final bool foodMode;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: InkWell(
        onTap: onTap,
        child: Row(
          children: [
            Container(
              width: 70,
              height: 70,
              decoration: BoxDecoration(
                color: product.color.withValues(alpha: 0.14),
                borderRadius: BorderRadius.circular(16),
              ),
              child: Icon(product.icon, color: product.color, size: 34),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    product.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontWeight: FontWeight.w900,
                      fontSize: 17,
                    ),
                  ),
                  Text(product.category, style: const TextStyle(color: _muted)),
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 10,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      Text(
                        product.price,
                        style: const TextStyle(
                          fontWeight: FontWeight.w900,
                          fontSize: 16,
                        ),
                      ),
                      Text(
                        foodMode
                            ? '20 min'
                            : (product.stock == 0
                                  ? 'Rupture'
                                  : '${product.stock} en stock'),
                        style: TextStyle(
                          color: product.color,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Column(
              children: [
                StatusBadge(product.status, color: product.color),
                IconButton(
                  onPressed: onEdit,
                  icon: const Icon(Icons.edit_outlined),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class RestaurantDishCard extends StatelessWidget {
  const RestaurantDishCard({
    super.key,
    required this.name,
    required this.meta,
    required this.price,
    required this.status,
    required this.color,
    required this.imageColor,
    required this.onTap,
  });

  final String name;
  final String meta;
  final String price;
  final String status;
  final Color color;
  final Color imageColor;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: EdgeInsets.zero,
      child: InkWell(
        borderRadius: BorderRadius.circular(18),
        onTap: onTap,
        child: Row(
          children: [
            ClipRRect(
              borderRadius: const BorderRadius.horizontal(
                left: Radius.circular(18),
              ),
              child: Container(
                width: 96,
                height: 112,
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: [
                      imageColor.withValues(alpha: 0.95),
                      imageColor.withValues(alpha: 0.45),
                    ],
                  ),
                ),
                child: const Icon(
                  Icons.restaurant_rounded,
                  color: Colors.white,
                  size: 38,
                ),
              ),
            ),
            Expanded(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 12, 12, 10),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontWeight: FontWeight.w900,
                              fontSize: 16,
                            ),
                          ),
                        ),
                        StatusBadge(status, color: color, compact: true),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      meta,
                      style: const TextStyle(color: _muted, fontSize: 13),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      price,
                      style: const TextStyle(
                        color: Color(0xFFFF4B00),
                        fontWeight: FontWeight.w900,
                        fontSize: 16,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Row(
                      children: [
                        SmallActionPill(
                          icon: Icons.edit_outlined,
                          label: 'Modifier',
                          color: _muted,
                          background: _bg,
                          onTap: () {},
                        ),
                        const SizedBox(width: 8),
                        SmallActionPill(
                          icon: Icons.delete_outline_rounded,
                          label: 'Suppr.',
                          color: _red,
                          background: const Color(0xFFFFF1F2),
                          onTap: () {},
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class FoodOrderCard extends StatelessWidget {
  const FoodOrderCard({
    super.key,
    required this.id,
    required this.client,
    required this.type,
    required this.time,
    required this.items,
    required this.status,
    required this.amount,
    required this.eta,
    required this.action,
    this.highlighted = false,
    this.statusColor = _orange,
  });

  final String id;
  final String client;
  final String type;
  final String time;
  final String items;
  final String status;
  final String amount;
  final String eta;
  final String action;
  final bool highlighted;
  final Color statusColor;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: EdgeInsets.zero,
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(18),
          border: highlighted
              ? Border.all(color: _red.withValues(alpha: 0.4))
              : null,
        ),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        width: 8,
                        height: 8,
                        margin: const EdgeInsets.only(top: 7),
                        decoration: const BoxDecoration(
                          color: _red,
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Flexible(
                                  child: Text(
                                    id,
                                    style: const TextStyle(
                                      fontSize: 17,
                                      fontWeight: FontWeight.w900,
                                    ),
                                  ),
                                ),
                                const SizedBox(width: 8),
                                StatusBadge(
                                  status,
                                  color: statusColor,
                                  compact: true,
                                ),
                              ],
                            ),
                            const SizedBox(height: 4),
                            Text(
                              '$client · $type · $time',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                color: _muted,
                                fontSize: 13,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(width: 8),
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            amount,
                            style: const TextStyle(
                              fontWeight: FontWeight.w900,
                              fontSize: 17,
                            ),
                          ),
                          if (eta.isNotEmpty)
                            Text(
                              eta,
                              style: const TextStyle(
                                color: Color(0xFFFF4B00),
                                fontWeight: FontWeight.w900,
                              ),
                            ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  Text(
                    items,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: _muted),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),
            Row(
              children: [
                Expanded(
                  child: TextButton.icon(
                    onPressed: () {},
                    icon: const Icon(Icons.visibility_outlined, size: 16),
                    label: const Text('Détails'),
                  ),
                ),
                Container(width: 1, height: 45, color: _border),
                Expanded(
                  child: TextButton.icon(
                    onPressed: () {},
                    icon: const Icon(
                      Icons.check_rounded,
                      size: 16,
                      color: Color(0xFFFF4B00),
                    ),
                    label: Text(
                      action,
                      style: const TextStyle(
                        color: Color(0xFFFF4B00),
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class MarketplaceOrderCard extends StatelessWidget {
  const MarketplaceOrderCard({
    super.key,
    required this.order,
    required this.onTap,
  });

  final Order order;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: InkWell(
        borderRadius: BorderRadius.circular(18),
        onTap: onTap,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              width: 8,
              height: 8,
              margin: const EdgeInsets.only(top: 8),
              decoration: const BoxDecoration(
                color: _red,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          order.id,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 17,
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      const StatusBadge(
                        'Nouvelle',
                        color: _blue,
                        compact: true,
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    order.client,
                    style: const TextStyle(
                      color: Color(0xFF475467),
                      fontSize: 15,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      const Icon(
                        Icons.schedule_rounded,
                        size: 14,
                        color: Color(0xFF98A2B3),
                      ),
                      const SizedBox(width: 4),
                      Text(
                        order.time,
                        style: const TextStyle(
                          color: Color(0xFF98A2B3),
                          fontSize: 13,
                        ),
                      ),
                      const SizedBox(width: 14),
                      Text(
                        '${order.items} articles',
                        style: const TextStyle(
                          color: Color(0xFF98A2B3),
                          fontSize: 13,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Text(
              order.amount,
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w900),
            ),
          ],
        ),
      ),
    );
  }
}

class OrderCard extends StatelessWidget {
  const OrderCard({
    super.key,
    required this.order,
    required this.restaurant,
    this.onTap,
  });

  final Order order;
  final bool restaurant;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: InkWell(
        onTap: onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    order.id,
                    style: const TextStyle(
                      fontWeight: FontWeight.w900,
                      fontSize: 17,
                    ),
                  ),
                ),
                StatusBadge(order.status, color: order.urgent ? _red : _green),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              order.client,
              style: const TextStyle(fontWeight: FontWeight.w700),
            ),
            Text(
              '${order.items} ${restaurant ? 'plats' : 'articles'} · ${order.time} · ${order.amount}',
              style: const TextStyle(color: _muted),
            ),
            if (restaurant) ...[
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: HbaButton(
                      label: 'Accepter',
                      compact: true,
                      color: _orange,
                      onPressed: () {},
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: HbaButton(
                      label: 'Prête',
                      compact: true,
                      outlined: true,
                      onPressed: () {},
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class MenuRow extends StatelessWidget {
  const MenuRow(
    this.icon,
    this.color,
    this.title,
    this.subtitle,
    this.onTap, {
    super.key,
  });

  final IconData icon;
  final Color color;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return TransparentMaterial(
      child: ListTile(
        leading: IconBox(icon: icon, color: color),
        title: Text(title, style: const TextStyle(fontWeight: FontWeight.w800)),
        subtitle: Text(subtitle, maxLines: 1, overflow: TextOverflow.ellipsis),
        trailing: const Icon(Icons.chevron_right_rounded, color: _muted),
        onTap: onTap,
      ),
    );
  }
}

class SearchAndFilter extends StatelessWidget {
  const SearchAndFilter({super.key, required this.hint, this.filter = true});

  final String hint;
  final bool filter;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: TextField(
            decoration: inputDecoration(hint).copyWith(
              prefixIcon: const Icon(Icons.search_rounded),
              labelText: null,
              hintText: hint,
            ),
          ),
        ),
        if (filter) ...[
          const SizedBox(width: 12),
          IconButton.outlined(
            onPressed: () {},
            icon: const Icon(Icons.filter_list_rounded),
          ),
        ],
      ],
    );
  }
}

class StateChips extends StatelessWidget {
  const StateChips({super.key, required this.state, required this.onChanged});

  final DemoListState state;
  final ValueChanged<DemoListState> onChanged;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        _StateChip(
          label: 'Chargement',
          state: DemoListState.loading,
          selected: state == DemoListState.loading,
          onTap: onChanged,
        ),
        const SizedBox(width: 8),
        _StateChip(
          label: 'Erreur',
          state: DemoListState.error,
          selected: state == DemoListState.error,
          onTap: onChanged,
        ),
        const SizedBox(width: 8),
        _StateChip(
          label: 'Vide',
          state: DemoListState.empty,
          selected: state == DemoListState.empty,
          onTap: onChanged,
        ),
      ],
    );
  }
}

class _StateChip extends StatelessWidget {
  const _StateChip({
    required this.label,
    required this.state,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final DemoListState state;
  final bool selected;
  final ValueChanged<DemoListState> onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(8),
      onTap: () => onTap(selected ? DemoListState.content : state),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          color: const Color(0xFFF0F2F5),
          borderRadius: BorderRadius.circular(8),
          border: selected ? Border.all(color: _blue, width: 1.6) : null,
        ),
        child: Text(
          '∘ $label',
          style: const TextStyle(
            color: Color(0xFF475467),
            fontSize: 12,
            fontWeight: FontWeight.w700,
          ),
        ),
      ),
    );
  }
}

class SkeletonList extends StatelessWidget {
  const SkeletonList({super.key, required this.itemCount});

  final int itemCount;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: List.generate(
        itemCount,
        (_) => HbaCard(
          child: Row(
            children: [
              const SkeletonBox(width: 42, height: 42),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: const [
                    SkeletonBox(width: 150, height: 16),
                    SizedBox(height: 10),
                    SkeletonBox(width: 100, height: 12),
                    SizedBox(height: 14),
                    SkeletonBox(width: 210, height: 12),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              const SkeletonBox(width: 64, height: 24),
            ],
          ),
        ),
      ),
    );
  }
}

class SkeletonBox extends StatelessWidget {
  const SkeletonBox({super.key, required this.width, required this.height});

  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: const Color(0xFFE5E7EB),
        borderRadius: BorderRadius.circular(6),
      ),
    );
  }
}

class EmptyStateView extends StatelessWidget {
  const EmptyStateView({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.actionLabel,
    this.onAction,
  });

  final IconData icon;
  final String title;
  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 72),
      child: Column(
        children: [
          IconBox(icon: icon, color: _muted, size: 64),
          const SizedBox(height: 18),
          Text(
            title,
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 8),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: _muted),
          ),
          if (actionLabel != null && onAction != null) ...[
            const SizedBox(height: 24),
            HbaButton(label: actionLabel!, onPressed: onAction!),
          ],
        ],
      ),
    );
  }
}

class ErrorStateView extends StatelessWidget {
  const ErrorStateView({
    super.key,
    required this.title,
    required this.message,
    required this.onRetry,
  });

  final String title;
  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 72),
      child: Column(
        children: [
          const IconBox(icon: Icons.wifi_off_rounded, color: _red, size: 64),
          const SizedBox(height: 18),
          Text(
            title,
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 8),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: _muted),
          ),
          const SizedBox(height: 24),
          FilledButton.icon(
            style: FilledButton.styleFrom(
              backgroundColor: _text,
              foregroundColor: Colors.white,
              minimumSize: const Size(140, 48),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
            ),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded),
            label: const Text(
              'Réessayer',
              style: TextStyle(fontWeight: FontWeight.w900),
            ),
          ),
        ],
      ),
    );
  }
}

class OrangeChoice extends StatelessWidget {
  const OrangeChoice({super.key, required this.label, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      alignment: Alignment.center,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: selected ? const Color(0xFFFF5A00) : Colors.white,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: selected ? const Color(0xFFFF5A00) : const Color(0xFFD0D5DD),
        ),
      ),
      child: Text(
        label,
        textAlign: TextAlign.center,
        style: TextStyle(
          color: selected ? Colors.white : const Color(0xFF344054),
          fontWeight: FontWeight.w700,
          fontSize: 13,
        ),
      ),
    );
  }
}

class GreenChoice extends StatelessWidget {
  const GreenChoice({
    super.key,
    required this.label,
    this.selected = false,
    this.soft = false,
  });

  final String label;
  final bool selected;
  final bool soft;

  @override
  Widget build(BuildContext context) {
    return Container(
      alignment: Alignment.center,
      padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 9),
      decoration: BoxDecoration(
        color: selected
            ? _green
            : (soft ? _green.withValues(alpha: 0.12) : Colors.white),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: selected || soft
              ? _green.withValues(alpha: 0.35)
              : const Color(0xFFD0D5DD),
        ),
      ),
      child: Text(
        label,
        textAlign: TextAlign.center,
        style: TextStyle(
          color: selected ? Colors.white : const Color(0xFF344054),
          fontWeight: FontWeight.w700,
          fontSize: 13,
        ),
      ),
    );
  }
}

class InfoBanner extends StatelessWidget {
  const InfoBanner(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: const Color(0xFFF8FAFC),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: _border),
      ),
      child: Row(
        children: [
          const Icon(Icons.info_outline_rounded, color: _muted, size: 18),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              style: const TextStyle(
                color: Color(0xFF475467),
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class ProductCalculatedSummary extends StatelessWidget {
  const ProductCalculatedSummary({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      color: const Color(0xFFECFDF5),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'RÉSUMÉ CALCULÉ',
            style: TextStyle(
              color: Color(0xFF047857),
              fontWeight: FontWeight.w900,
              fontSize: 13,
            ),
          ),
          SizedBox(height: 12),
          SummaryLine(label: 'Prix affiché client', value: '—'),
          SummaryLine(label: 'Marge estimée', value: '—'),
          SummaryLine(label: 'Statut stock', value: '—'),
        ],
      ),
    );
  }
}

class ProductPublicationChecklist extends StatelessWidget {
  const ProductPublicationChecklist({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SmallSectionTitle('CHECKLIST AVANT PUBLICATION'),
          const ChecklistRow('Informations complètes'),
          const ChecklistRow('Prix renseigné'),
          const ChecklistRow('Stock renseigné'),
          const ChecklistRow('Photo principale ajoutée'),
          Row(
            children: const [
              Icon(Icons.cancel_rounded, color: Color(0xFFD0D5DD)),
              SizedBox(width: 12),
              Text(
                'Attributs catégorie complétés',
                style: TextStyle(color: Color(0xFF98A2B3), fontSize: 16),
              ),
            ],
          ),
          const SizedBox(height: 16),
          HbaButton(
            label: 'Aperçu de la fiche produit',
            outlined: true,
            onPressed: () {},
          ),
        ],
      ),
    );
  }
}

class ProductPublishDialog extends StatelessWidget {
  const ProductPublishDialog({super.key});

  @override
  Widget build(BuildContext context) {
    return Dialog(
      insetPadding: const EdgeInsets.symmetric(horizontal: 44),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: const [
                IconBox(icon: Icons.check_rounded, color: _green, size: 52),
                SizedBox(height: 24),
                Text(
                  'Publier ce produit ?',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
                ),
                SizedBox(height: 12),
                Text(
                  '"eeeee" sera visible dans votre catalogue.',
                  style: TextStyle(color: _muted, height: 1.4),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          Row(
            children: [
              Expanded(
                child: TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const Text('Annuler'),
                ),
              ),
              Container(width: 1, height: 52, color: _border),
              Expanded(
                child: TextButton(
                  onPressed: () => Navigator.pop(context, true),
                  child: const Text(
                    'Publier',
                    style: TextStyle(
                      color: _green,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class DishPhotoBox extends StatelessWidget {
  const DishPhotoBox({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 118,
      decoration: BoxDecoration(
        color: const Color(0xFFFFF7ED),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: const Color(0xFFFFB86A),
          style: BorderStyle.solid,
        ),
      ),
      child: const Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.camera_alt_outlined, color: Color(0xFFFF5A00), size: 28),
            SizedBox(height: 10),
            Text(
              'Ajouter une photo du plat',
              style: TextStyle(
                color: Color(0xFFFF5A00),
                fontWeight: FontWeight.w900,
                fontSize: 12,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class FormRadioRow extends StatelessWidget {
  const FormRadioRow(this.label, {super.key, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 13),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF2F4F7))),
      ),
      child: Row(
        children: [
          Icon(
            selected
                ? Icons.radio_button_checked_rounded
                : Icons.radio_button_off_rounded,
            color: selected ? const Color(0xFFFF5A00) : const Color(0xFFD0D5DD),
          ),
          const SizedBox(width: 12),
          Text(
            label,
            style: const TextStyle(color: Color(0xFF344054), fontSize: 16),
          ),
        ],
      ),
    );
  }
}

class ExtraRow extends StatelessWidget {
  const ExtraRow(this.name, this.price, {super.key});

  final String name;
  final String price;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: HbaTextField(label: '', initial: name),
        ),
        const SizedBox(width: 8),
        SizedBox(
          width: 66,
          child: HbaTextField(label: '', initial: price),
        ),
        const SizedBox(width: 8),
        const Text('F', style: TextStyle(color: Color(0xFF98A2B3))),
      ],
    );
  }
}

class ChoiceSummary extends StatelessWidget {
  const ChoiceSummary({super.key, required this.title, required this.value});

  final String title;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 10),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF2F4F7))),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: const TextStyle(
              fontWeight: FontWeight.w800,
              color: Color(0xFF344054),
            ),
          ),
          const SizedBox(height: 3),
          Text(value, style: const TextStyle(color: Color(0xFF98A2B3))),
        ],
      ),
    );
  }
}

class FormSwitchRow extends StatelessWidget {
  const FormSwitchRow(this.label, {super.key, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 10),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF2F4F7))),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              label,
              style: const TextStyle(color: Color(0xFF344054), fontSize: 16),
            ),
          ),
          Switch(value: selected, activeThumbColor: _green, onChanged: (_) {}),
        ],
      ),
    );
  }
}

class ChecklistRow extends StatelessWidget {
  const ChecklistRow(this.label, {super.key});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Row(
        children: [
          Container(
            width: 22,
            height: 22,
            decoration: const BoxDecoration(
              color: Color(0xFF10B981),
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.check_rounded,
              color: Colors.white,
              size: 15,
            ),
          ),
          const SizedBox(width: 12),
          Text(
            label,
            style: const TextStyle(color: Color(0xFF344054), fontSize: 16),
          ),
        ],
      ),
    );
  }
}

class ChoiceListTile extends StatelessWidget {
  const ChoiceListTile({
    super.key,
    required this.icon,
    required this.title,
    required this.selected,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(16),
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: selected ? _green.withValues(alpha: 0.1) : _bg,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: selected ? _green : _border),
        ),
        child: Row(
          children: [
            IconBox(icon: icon, color: selected ? _green : _muted),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                title,
                style: const TextStyle(fontWeight: FontWeight.w900),
              ),
            ),
            Icon(
              selected ? Icons.check_circle_rounded : Icons.circle_outlined,
              color: selected ? _green : _muted,
            ),
          ],
        ),
      ),
    );
  }
}

class SmallActionPill extends StatelessWidget {
  const SmallActionPill({
    super.key,
    required this.icon,
    required this.label,
    required this.color,
    required this.background,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final Color color;
  final Color background;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(8),
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 14, color: color),
            const SizedBox(width: 4),
            Text(
              label,
              style: TextStyle(
                color: color,
                fontWeight: FontWeight.w700,
                fontSize: 12,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class FilterChips extends StatelessWidget {
  const FilterChips({
    super.key,
    required this.labels,
    this.activeColor = _green,
  });

  final List<String> labels;
  final Color activeColor;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (var i = 0; i < labels.length; i++)
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: ChoiceChip(
                label: Text(labels[i]),
                selected: i == 0,
                selectedColor: activeColor,
                labelStyle: TextStyle(
                  color: i == 0 ? Colors.white : _text,
                  fontWeight: FontWeight.w700,
                ),
                onSelected: (_) {},
              ),
            ),
        ],
      ),
    );
  }
}

class HbaButton extends StatelessWidget {
  const HbaButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.outlined = false,
    this.compact = false,
    this.color = _green,
  });

  final String label;
  final VoidCallback onPressed;
  final bool outlined;
  final bool compact;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(14),
    );
    if (outlined) {
      return OutlinedButton(
        style: OutlinedButton.styleFrom(
          foregroundColor: color,
          side: BorderSide(color: color.withValues(alpha: 0.45)),
          minimumSize: Size.fromHeight(compact ? 42 : 52),
          shape: shape,
        ),
        onPressed: onPressed,
        child: Text(
          label,
          textAlign: TextAlign.center,
          style: const TextStyle(fontWeight: FontWeight.w800),
        ),
      );
    }
    return FilledButton(
      style: FilledButton.styleFrom(
        backgroundColor: color,
        foregroundColor: Colors.white,
        minimumSize: Size.fromHeight(compact ? 42 : 52),
        shape: shape,
      ),
      onPressed: onPressed,
      child: Text(
        label,
        textAlign: TextAlign.center,
        style: const TextStyle(fontWeight: FontWeight.w800),
      ),
    );
  }
}

class HbaTextField extends StatelessWidget {
  const HbaTextField({
    super.key,
    required this.label,
    this.initial,
    this.hint,
    this.maxLines = 1,
  });

  final String label;
  final String? initial;
  final String? hint;
  final int maxLines;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      initialValue: initial,
      maxLines: maxLines,
      decoration: inputDecoration(label).copyWith(hintText: hint),
    );
  }
}

class StatusBadge extends StatelessWidget {
  const StatusBadge(
    this.label, {
    super.key,
    required this.color,
    this.compact = false,
  });

  final String label;
  final Color color;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: compact ? 7 : 10,
        vertical: compact ? 4 : 6,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.11),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color.withValues(alpha: 0.28)),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: color,
          fontWeight: FontWeight.w800,
          fontSize: compact ? 12 : null,
        ),
      ),
    );
  }
}

class IconBox extends StatelessWidget {
  const IconBox({
    super.key,
    required this.icon,
    required this.color,
    this.size = 48,
  });

  final IconData icon;
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Icon(icon, color: color),
    );
  }
}

class HomeHeader extends StatelessWidget {
  const HomeHeader({
    super.key,
    required this.greeting,
    required this.shop,
    required this.onSelectShop,
    required this.onNotifications,
  });

  final String greeting;
  final Shop shop;
  final VoidCallback onSelectShop;
  final VoidCallback onNotifications;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(20, 14, 20, 12),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(bottom: BorderSide(color: _border)),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(greeting, style: const TextStyle(color: _muted)),
                const SizedBox(height: 4),
                InkWell(
                  borderRadius: BorderRadius.circular(10),
                  onTap: onSelectShop,
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        shop.type == ShopType.restaurant
                            ? Icons.receipt_long_outlined
                            : Icons.home_outlined,
                        size: 16,
                        color: _text,
                      ),
                      const SizedBox(width: 5),
                      Flexible(
                        child: Text(
                          shop.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontWeight: FontWeight.w900,
                            fontSize: 16,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      StatusBadge(
                        shop.statusLabel,
                        color: shop.statusColor,
                        compact: true,
                      ),
                      const SizedBox(width: 3),
                      const Icon(
                        Icons.keyboard_arrow_down_rounded,
                        color: _muted,
                        size: 18,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          IconButton.outlined(
            onPressed: onNotifications,
            icon: Badge(
              smallSize: 8,
              backgroundColor: _red,
              child: const Icon(Icons.notifications_none_rounded),
            ),
          ),
        ],
      ),
    );
  }
}

class SmallSectionTitle extends StatelessWidget {
  const SmallSectionTitle(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Text(
        text,
        style: const TextStyle(
          color: _muted,
          fontWeight: FontWeight.w900,
          fontSize: 13,
          letterSpacing: 0,
        ),
      ),
    );
  }
}

class HomeStatsGrid extends StatelessWidget {
  const HomeStatsGrid({super.key, required this.cards});

  final List<HomeStatCard> cards;

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 2,
      mainAxisSpacing: 12,
      crossAxisSpacing: 12,
      childAspectRatio: 1.9,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      children: cards,
    );
  }
}

class HomeStatCard extends StatelessWidget {
  const HomeStatCard({super.key, required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      margin: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(color: _muted, fontSize: 13),
          ),
          const SizedBox(height: 8),
          Text(
            value,
            style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class AlertRow extends StatelessWidget {
  const AlertRow({
    super.key,
    required this.icon,
    required this.text,
    required this.color,
    required this.background,
    required this.onTap,
  });

  final IconData icon;
  final String text;
  final Color color;
  final Color background;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
          decoration: BoxDecoration(
            color: background,
            borderRadius: BorderRadius.circular(14),
          ),
          child: Row(
            children: [
              Icon(icon, color: color, size: 19),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  text,
                  style: TextStyle(color: color, fontWeight: FontWeight.w700),
                ),
              ),
              Text(
                'Voir →',
                style: TextStyle(color: color, fontWeight: FontWeight.w900),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class RecentOrderTile extends StatelessWidget {
  const RecentOrderTile({super.key, required this.order});

  final Order order;

  @override
  Widget build(BuildContext context) {
    final color = order.status == 'Préparation' ? _orange : _blue;
    return HbaCard(
      child: Row(
        children: [
          Container(
            width: 8,
            height: 8,
            decoration: const BoxDecoration(
              color: _red,
              shape: BoxShape.circle,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        order.id,
                        style: const TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w900,
                        ),
                      ),
                    ),
                    StatusBadge(order.status, color: color, compact: true),
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  '${order.client} · ${order.items} art. · ${order.time}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: _muted, fontSize: 13),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          Text(
            order.amount,
            style: const TextStyle(fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class StatTile extends StatelessWidget {
  const StatTile(this.label, this.value, this.icon, this.color, {super.key});

  final String label;
  final String value;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      margin: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color),
          const SizedBox(height: 10),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 20),
          ),
          Text(label, style: const TextStyle(color: _muted, fontSize: 12)),
        ],
      ),
    );
  }
}

class QuickAction extends StatelessWidget {
  const QuickAction({
    super.key,
    required this.icon,
    required this.label,
    required this.color,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
      margin: EdgeInsets.zero,
      child: InkWell(
        onTap: onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.center,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            IconBox(icon: icon, color: color, size: 36),
            const SizedBox(height: 10),
            Text(
              label,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              textAlign: TextAlign.center,
              style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 12),
            ),
          ],
        ),
      ),
    );
  }
}

class SectionHeader extends StatelessWidget {
  const SectionHeader({
    super.key,
    required this.title,
    required this.action,
    required this.onTap,
  });

  final String title;
  final String action;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        children: [
          Expanded(child: Text(title, style: _titleStyle)),
          if (action.isNotEmpty)
            TextButton(
              onPressed: onTap,
              child: Text(
                action,
                style: const TextStyle(fontWeight: FontWeight.w800),
              ),
            ),
        ],
      ),
    );
  }
}

class RestaurantStatusCard extends StatelessWidget {
  const RestaurantStatusCard({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'RESTAURANT',
            style: TextStyle(
              color: _muted,
              fontWeight: FontWeight.w900,
              fontSize: 13,
            ),
          ),
          Row(
            children: [
              const Expanded(
                child: Row(
                  children: [
                    Icon(Icons.circle, color: _green, size: 8),
                    SizedBox(width: 8),
                    Text(
                      'Ouvert',
                      style: TextStyle(
                        fontWeight: FontWeight.w900,
                        fontSize: 17,
                      ),
                    ),
                  ],
                ),
              ),
              Switch(value: true, activeThumbColor: _green, onChanged: (_) {}),
            ],
          ),
          const Divider(height: 18),
          Row(
            children: [
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Accepte les commandes',
                      style: TextStyle(
                        fontWeight: FontWeight.w800,
                        fontSize: 16,
                      ),
                    ),
                    Text(
                      'Nouvelles commandes entrantes',
                      style: TextStyle(color: _muted),
                    ),
                  ],
                ),
              ),
              Switch(value: true, activeThumbColor: _green, onChanged: (_) {}),
            ],
          ),
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            decoration: BoxDecoration(
              color: _bg,
              borderRadius: BorderRadius.circular(12),
            ),
            child: const Row(
              children: [
                Icon(Icons.schedule_rounded, color: _orange, size: 18),
                SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Temps moyen préparation',
                    style: TextStyle(
                      color: _muted,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                Text(
                  '25 min',
                  style: TextStyle(color: _orange, fontWeight: FontWeight.w900),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class RestaurantMetricCard extends StatelessWidget {
  const RestaurantMetricCard(
    this.icon,
    this.value,
    this.label,
    this.color, {
    super.key,
  });

  final IconData icon;
  final String value;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
      margin: EdgeInsets.zero,
      child: Column(
        children: [
          IconBox(icon: icon, color: color, size: 36),
          const SizedBox(height: 8),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 15),
          ),
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(color: _muted, fontSize: 10),
          ),
        ],
      ),
    );
  }
}

class FoodUrgentCard extends StatelessWidget {
  const FoodUrgentCard({
    super.key,
    required this.id,
    required this.detail,
    required this.status,
    required this.amount,
    required this.eta,
  });

  final String id;
  final String detail;
  final String status;
  final String amount;
  final String eta;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      color: Colors.white,
      child: Container(
        decoration: BoxDecoration(
          border: Border.all(color: _orange.withValues(alpha: 0.35)),
          borderRadius: BorderRadius.circular(16),
        ),
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Container(
                  width: 8,
                  height: 8,
                  decoration: BoxDecoration(
                    color: _red.withValues(alpha: 0.75),
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    id,
                    style: const TextStyle(
                      fontWeight: FontWeight.w900,
                      fontSize: 18,
                    ),
                  ),
                ),
                StatusBadge(eta, color: _orange, compact: true),
              ],
            ),
            const SizedBox(height: 10),
            Text(
              detail,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(color: _muted),
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                StatusBadge(status, color: _orange, compact: true),
                const Spacer(),
                Text(
                  amount,
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class AvailabilityCard extends StatelessWidget {
  const AvailabilityCard({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'Disponibilité plats',
                  style: TextStyle(fontWeight: FontWeight.w900),
                ),
              ),
              TextButton(
                onPressed: () {},
                child: const Text(
                  'Gérer →',
                  style: TextStyle(fontWeight: FontWeight.w900),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Container(
                width: 8,
                height: 8,
                decoration: const BoxDecoration(
                  color: _red,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 12),
              const Expanded(
                child: Text(
                  'Pâte rouge et légumes',
                  style: TextStyle(color: _muted, fontSize: 16),
                ),
              ),
              const StatusBadge('Indisponible', color: _red, compact: true),
            ],
          ),
        ],
      ),
    );
  }
}

class ProductHero extends StatelessWidget {
  const ProductHero({super.key, required this.product});

  final Product product;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            height: 190,
            decoration: BoxDecoration(
              color: product.color.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Center(
              child: Icon(product.icon, size: 78, color: product.color),
            ),
          ),
          const SizedBox(height: 14),
          StatusBadge(product.status, color: product.color),
          const SizedBox(height: 10),
          Text(
            product.name,
            style: const TextStyle(fontSize: 23, fontWeight: FontWeight.w900),
          ),
          Text(
            product.price,
            style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class InfoCard extends StatelessWidget {
  const InfoCard({super.key, required this.title, required this.lines});

  final String title;
  final List<String> lines;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: _titleStyle),
          const SizedBox(height: 10),
          ...lines.map(
            (line) => Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(
                line,
                style: const TextStyle(color: _muted, height: 1.35),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class SummaryLine extends StatelessWidget {
  const SummaryLine({super.key, required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        children: [
          Expanded(
            child: Text(label, style: const TextStyle(color: _muted)),
          ),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: const TextStyle(fontWeight: FontWeight.w800),
            ),
          ),
        ],
      ),
    );
  }
}

class StepHeader extends StatelessWidget {
  const StepHeader({
    super.key,
    required this.step,
    required this.total,
    required this.labels,
  });

  final int step;
  final int total;
  final List<String> labels;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Étape ${step + 1} sur $total — ${labels[step]}',
          style: const TextStyle(fontWeight: FontWeight.w900),
        ),
        const SizedBox(height: 10),
        ClipRRect(
          borderRadius: BorderRadius.circular(99),
          child: LinearProgressIndicator(
            value: (step + 1) / total,
            minHeight: 8,
            backgroundColor: _border,
            color: _green,
          ),
        ),
      ],
    );
  }
}

class FormFooter extends StatelessWidget {
  const FormFooter({
    super.key,
    required this.canGoBack,
    required this.primaryLabel,
    required this.secondaryLabel,
    required this.onBack,
    required this.onPrimary,
  });

  final bool canGoBack;
  final String primaryLabel;
  final String secondaryLabel;
  final VoidCallback onBack;
  final VoidCallback onPrimary;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(
        20,
        12,
        20,
        MediaQuery.of(context).padding.bottom + 12,
      ),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: _border)),
      ),
      child: Row(
        children: [
          if (canGoBack)
            Expanded(
              child: HbaButton(
                label: secondaryLabel,
                outlined: true,
                onPressed: onBack,
              ),
            )
          else
            const Spacer(),
          const SizedBox(width: 12),
          Expanded(
            child: HbaButton(label: primaryLabel, onPressed: onPrimary),
          ),
        ],
      ),
    );
  }
}

class ConversationTile extends StatelessWidget {
  const ConversationTile({super.key, required this.conversation});

  final Conversation conversation;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: TransparentMaterial(
        child: ListTile(
          contentPadding: EdgeInsets.zero,
          leading: CircleAvatar(
            backgroundColor: _green.withValues(alpha: 0.12),
            child: Text(
              conversation.name.substring(0, 1),
              style: const TextStyle(
                color: _green,
                fontWeight: FontWeight.w900,
              ),
            ),
          ),
          title: Text(
            conversation.name,
            style: const TextStyle(fontWeight: FontWeight.w900),
          ),
          subtitle: Text(
            conversation.orderId == null
                ? conversation.message
                : '${conversation.orderId} · ${conversation.message}',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          trailing: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                conversation.time,
                style: const TextStyle(color: _muted, fontSize: 12),
              ),
              if (conversation.unread > 0)
                Badge.count(count: conversation.unread, backgroundColor: _red),
            ],
          ),
          onTap: () =>
              pushPage(context, ConversationScreen(conversation: conversation)),
        ),
      ),
    );
  }
}

class MessageBubble extends StatelessWidget {
  const MessageBubble({
    super.key,
    required this.text,
    required this.incoming,
    required this.time,
  });

  final String text;
  final bool incoming;
  final String time;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: incoming ? Alignment.centerLeft : Alignment.centerRight,
      child: Container(
        constraints: BoxConstraints(
          maxWidth: MediaQuery.of(context).size.width * 0.74,
        ),
        margin: const EdgeInsets.only(bottom: 12),
        padding: const EdgeInsets.all(13),
        decoration: BoxDecoration(
          color: incoming ? Colors.white : _green.withValues(alpha: 0.15),
          borderRadius: BorderRadius.circular(16),
          border: Border.all(
            color: incoming ? _border : _green.withValues(alpha: 0.2),
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(text),
            const SizedBox(height: 5),
            Text(time, style: const TextStyle(color: _muted, fontSize: 11)),
          ],
        ),
      ),
    );
  }
}

class ChatInputBar extends StatelessWidget {
  const ChatInputBar({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(
        12,
        10,
        12,
        MediaQuery.of(context).padding.bottom + 10,
      ),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: _border)),
      ),
      child: Row(
        children: [
          IconButton(
            onPressed: () {},
            icon: const Icon(Icons.attach_file_rounded),
          ),
          const Expanded(
            child: TextField(
              decoration: InputDecoration(
                hintText: 'Écrire un message...',
                border: InputBorder.none,
              ),
            ),
          ),
          IconButton.filled(
            style: IconButton.styleFrom(
              backgroundColor: _green,
              foregroundColor: Colors.white,
            ),
            onPressed: () {},
            icon: const Icon(Icons.send_rounded),
          ),
        ],
      ),
    );
  }
}

class ShopManageCard extends StatelessWidget {
  const ShopManageCard({super.key, required this.shop});

  final Shop shop;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBox(
                icon: shop.type == ShopType.restaurant
                    ? Icons.restaurant_rounded
                    : Icons.storefront_rounded,
                color: shop.type == ShopType.restaurant ? _orange : _green,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      shop.name,
                      style: const TextStyle(fontWeight: FontWeight.w900),
                    ),
                    Text(
                      '${shop.typeLabel} · ${shop.city}',
                      style: const TextStyle(color: _muted),
                    ),
                  ],
                ),
              ),
              StatusBadge(shop.statusLabel, color: shop.statusColor),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: StatMini(label: 'Commandes', value: '${shop.orders}'),
              ),
              Expanded(
                child: StatMini(label: 'Revenus', value: shop.revenue),
              ),
              Expanded(
                child: StatMini(label: 'Articles', value: '${shop.items}'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class StatMini extends StatelessWidget {
  const StatMini({super.key, required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontWeight: FontWeight.w900),
        ),
        Text(label, style: const TextStyle(color: _muted, fontSize: 12)),
      ],
    );
  }
}

class PaymentTile extends StatelessWidget {
  const PaymentTile(
    this.title,
    this.subtitle,
    this.status,
    this.color, {
    super.key,
  });

  final String title;
  final String subtitle;
  final String status;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: TransparentMaterial(
        child: ListTile(
          contentPadding: EdgeInsets.zero,
          leading: IconBox(
            icon: Icons.account_balance_wallet_outlined,
            color: color,
          ),
          title: Text(
            title,
            style: const TextStyle(fontWeight: FontWeight.w900),
          ),
          subtitle: Text(subtitle),
          trailing: StatusBadge(status, color: color),
        ),
      ),
    );
  }
}

class InvoiceTile extends StatelessWidget {
  const InvoiceTile(
    this.title,
    this.date,
    this.status,
    this.amount, {
    super.key,
  });

  final String title;
  final String date;
  final String status;
  final String amount;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: TransparentMaterial(
        child: ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const IconBox(
            icon: Icons.description_outlined,
            color: _blue,
          ),
          title: Text(
            title,
            style: const TextStyle(fontWeight: FontWeight.w900),
          ),
          subtitle: Text(date),
          trailing: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(amount, style: const TextStyle(fontWeight: FontWeight.w900)),
              Text(status, style: const TextStyle(color: _muted, fontSize: 12)),
            ],
          ),
        ),
      ),
    );
  }
}

class WithdrawSheet extends StatelessWidget {
  const WithdrawSheet({super.key});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        12,
        20,
        MediaQuery.of(context).viewInsets.bottom +
            MediaQuery.of(context).padding.bottom +
            20,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Center(
            child: Container(
              width: 44,
              height: 4,
              decoration: BoxDecoration(
                color: _border,
                borderRadius: BorderRadius.circular(999),
              ),
            ),
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              const Expanded(
                child: Text('Retrait de fonds', style: _titleStyle),
              ),
              IconButton.filledTonal(
                style: IconButton.styleFrom(backgroundColor: _bg),
                onPressed: () => Navigator.pop(context),
                icon: const Icon(Icons.close_rounded),
              ),
            ],
          ),
          const SizedBox(height: 18),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(18),
            decoration: BoxDecoration(
              color: _green.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(14),
            ),
            child: const Column(
              children: [
                Text(
                  'Solde disponible',
                  style: TextStyle(color: _green, fontSize: 12),
                ),
                SizedBox(height: 4),
                Text(
                  '45 000 FCFA',
                  style: TextStyle(
                    color: Color(0xFF047857),
                    fontSize: 24,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          const HbaTextField(
            label: 'Montant à retirer (FCFA)',
            initial: '20000',
          ),
          const SizedBox(height: 16),
          const Text('Moyen de retrait', style: _fieldLabelStyle),
          const SizedBox(height: 8),
          const WithdrawalMethod('MTN Mobile Money', selected: true),
          const WithdrawalMethod('Moov Money'),
          const WithdrawalMethod('Virement bancaire'),
          const SizedBox(height: 12),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: _orange.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(12),
            ),
            child: const Text(
              'ⓘ  Frais : 1% (min. 200 FCFA). Délai : 24h ouvrées.',
              style: TextStyle(
                color: Color(0xFFEA580C),
                fontSize: 12,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(height: 18),
          HbaButton(
            label: "Retirer l'argent",
            onPressed: () => Navigator.pop(context),
          ),
        ],
      ),
    );
  }
}

class WithdrawalMethod extends StatelessWidget {
  const WithdrawalMethod(this.label, {super.key, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
      decoration: BoxDecoration(
        color: _bg,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: _border),
      ),
      child: Row(
        children: [
          Icon(
            selected ? Icons.radio_button_checked : Icons.radio_button_off,
            color: selected ? _green : _muted.withValues(alpha: 0.55),
            size: 20,
          ),
          const SizedBox(width: 12),
          Text(label, style: const TextStyle(fontWeight: FontWeight.w700)),
        ],
      ),
    );
  }
}

class RatingSummaryCard extends StatelessWidget {
  const RatingSummaryCard({super.key});

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Row(
        children: [
          const SizedBox(
            width: 118,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '4.7',
                  style: TextStyle(fontSize: 40, fontWeight: FontWeight.w900),
                ),
                SizedBox(height: 12),
                Text('128 avis', style: TextStyle(color: _muted)),
              ],
            ),
          ),
          Expanded(
            child: Column(
              children: const [
                RatingBar(value: 0.7, count: '7'),
                RatingBar(value: 0.2, count: '2'),
                RatingBar(value: 0.04, count: '1'),
                RatingBar(value: 0, count: '0'),
                RatingBar(value: 0, count: '0'),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class RatingBar extends StatelessWidget {
  const RatingBar({super.key, required this.value, required this.count});

  final double value;
  final String count;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        children: [
          Expanded(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(
                value: value,
                minHeight: 5,
                backgroundColor: _bg,
                color: const Color(0xFFF59E0B),
              ),
            ),
          ),
          const SizedBox(width: 16),
          Text(count, style: const TextStyle(color: _muted)),
        ],
      ),
    );
  }
}

class StateChipsStatic extends StatelessWidget {
  const StateChipsStatic({super.key, required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: _bg,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        '⊘ $label',
        style: const TextStyle(
          color: _muted,
          fontSize: 12,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }
}

class ReviewCard extends StatelessWidget {
  const ReviewCard({
    super.key,
    required this.initial,
    required this.name,
    required this.date,
    required this.text,
    required this.product,
    this.replied = false,
  });

  final String initial;
  final String name;
  final String date;
  final String text;
  final String product;
  final bool replied;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              CircleAvatar(
                backgroundColor: _bg,
                child: Text(
                  initial,
                  style: const TextStyle(
                    color: _muted,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  name,
                  style: const TextStyle(fontWeight: FontWeight.w900),
                ),
              ),
              Text(date, style: const TextStyle(color: _muted, fontSize: 12)),
            ],
          ),
          const SizedBox(height: 14),
          Text(
            text,
            style: const TextStyle(color: Color(0xFF475569), height: 1.45),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              const Icon(Icons.inventory_2_outlined, size: 14, color: _muted),
              const SizedBox(width: 4),
              Expanded(
                child: Text(
                  product,
                  style: const TextStyle(color: _muted, fontSize: 12),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          if (replied)
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
              decoration: BoxDecoration(
                color: _green.withValues(alpha: 0.1),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: _green.withValues(alpha: 0.2)),
              ),
              child: const Text(
                '✓ Réponse publiée',
                style: TextStyle(color: _green, fontWeight: FontWeight.w800),
              ),
            )
          else
            const Text(
              '▱ Répondre',
              style: TextStyle(color: _green, fontWeight: FontWeight.w800),
            ),
        ],
      ),
    );
  }
}

class SegmentedHeader extends StatelessWidget {
  const SegmentedHeader({
    super.key,
    required this.labels,
    required this.selected,
    required this.onChanged,
  });

  final List<String> labels;
  final int selected;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      color: Colors.white,
      child: Row(
        children: [
          for (var index = 0; index < labels.length; index++)
            Expanded(
              child: InkWell(
                onTap: () => onChanged(index),
                child: Container(
                  padding: const EdgeInsets.only(top: 15, bottom: 13),
                  decoration: BoxDecoration(
                    border: Border(
                      bottom: BorderSide(
                        color: selected == index ? _green : Colors.transparent,
                        width: 2,
                      ),
                    ),
                  ),
                  child: Text(
                    labels[index],
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: selected == index ? _green : _muted,
                      fontWeight: selected == index
                          ? FontWeight.w800
                          : FontWeight.w600,
                      fontSize: 13,
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class DeliveryCard extends StatelessWidget {
  const DeliveryCard({
    super.key,
    required this.order,
    required this.status,
    required this.statusColor,
    required this.client,
    required this.address,
    required this.driver,
    required this.eta,
    required this.id,
    this.showDriver = true,
  });

  final String order;
  final String status;
  final Color statusColor;
  final String client;
  final String address;
  final String driver;
  final String eta;
  final String id;
  final bool showDriver;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(order, style: const TextStyle(fontWeight: FontWeight.w900)),
              const SizedBox(width: 8),
              StatusBadge(status, color: statusColor, compact: true),
              const Spacer(),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  if (eta.isNotEmpty)
                    Text(
                      eta,
                      style: const TextStyle(
                        color: _blue,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  Text(id, style: const TextStyle(color: _muted, fontSize: 12)),
                ],
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(client, style: const TextStyle(color: Color(0xFF475569))),
          const SizedBox(height: 4),
          Row(
            children: [
              const Icon(Icons.location_on_outlined, size: 14, color: _muted),
              const SizedBox(width: 4),
              Expanded(
                child: Text(
                  address,
                  style: const TextStyle(color: _muted, fontSize: 12),
                ),
              ),
            ],
          ),
          if (showDriver) ...[
            const Divider(height: 24),
            Row(
              children: [
                CircleAvatar(
                  radius: 16,
                  backgroundColor: _blue.withValues(alpha: 0.12),
                  child: Text(
                    driver.characters.first,
                    style: const TextStyle(
                      color: _blue,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(child: Text(driver)),
                TextButton(
                  onPressed: () {},
                  child: const Text(
                    'Voir le suivi →',
                    style: TextStyle(fontWeight: FontWeight.w900),
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class MemberCard extends StatelessWidget {
  const MemberCard({
    super.key,
    required this.initial,
    required this.name,
    required this.contact,
    required this.role,
    required this.color,
    this.inactive = false,
  });

  final String initial;
  final String name;
  final String contact;
  final String role;
  final Color color;
  final bool inactive;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Row(
        children: [
          CircleAvatar(
            radius: 24,
            backgroundColor: color.withValues(alpha: 0.18),
            child: Text(
              initial,
              style: TextStyle(color: color, fontWeight: FontWeight.w900),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        name,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w900),
                      ),
                    ),
                    if (inactive) ...[
                      const SizedBox(width: 6),
                      StatusBadge('Inactif', color: _muted, compact: true),
                    ],
                  ],
                ),
                Text(
                  contact,
                  style: const TextStyle(color: _muted, fontSize: 12),
                ),
                const SizedBox(height: 6),
                Align(
                  alignment: Alignment.centerLeft,
                  child: StatusBadge(role, color: color, compact: true),
                ),
              ],
            ),
          ),
          IconButton.filledTonal(
            style: IconButton.styleFrom(backgroundColor: _bg),
            onPressed: () {},
            icon: const Icon(Icons.more_horiz_rounded),
          ),
        ],
      ),
    );
  }
}

class PermissionsCard extends StatelessWidget {
  const PermissionsCard({super.key});

  @override
  Widget build(BuildContext context) {
    const rows = [
      ('Gérer produits', [true, true, false]),
      ('Gérer commandes', [true, true, true]),
      ('Gérer paiements', [true, false, false]),
      ('Voir statistiques', [true, true, false]),
    ];
    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SmallSectionTitle('PERMISSIONS PAR RÔLE'),
          const SizedBox(height: 12),
          for (final row in rows)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 7),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      row.$1,
                      style: const TextStyle(color: Color(0xFF475569)),
                    ),
                  ),
                  for (final allowed in row.$2)
                    Padding(
                      padding: const EdgeInsets.only(left: 10),
                      child: CircleAvatar(
                        radius: 10,
                        backgroundColor: allowed
                            ? _green.withValues(alpha: 0.18)
                            : _muted.withValues(alpha: 0.12),
                        child: Icon(
                          allowed ? Icons.check_rounded : Icons.close_rounded,
                          color: allowed ? _green : _muted,
                          size: 12,
                        ),
                      ),
                    ),
                ],
              ),
            ),
          const SizedBox(height: 8),
          const Align(
            alignment: Alignment.centerRight,
            child: Text(
              'Propr.   Mgr.   Prép.',
              style: TextStyle(color: _muted, fontSize: 10),
            ),
          ),
        ],
      ),
    );
  }
}

class PermissionCheckRow extends StatelessWidget {
  const PermissionCheckRow(this.label, {super.key, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 12),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: Color(0xFFF1F5F9))),
      ),
      child: Row(
        children: [
          Icon(
            selected
                ? Icons.check_box_rounded
                : Icons.check_box_outline_blank_rounded,
            color: selected ? _green : _muted.withValues(alpha: 0.55),
          ),
          const SizedBox(width: 12),
          Text(label, style: const TextStyle(color: Color(0xFF475569))),
        ],
      ),
    );
  }
}

class FinanceBalanceCard extends StatelessWidget {
  const FinanceBalanceCard({super.key, required this.onWithdraw});

  final VoidCallback onWithdraw;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      color: _green,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: const [
              Expanded(
                child: Text(
                  'Solde disponible',
                  style: TextStyle(
                    color: Colors.white70,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              IconBox(
                icon: Icons.credit_card_outlined,
                color: Colors.white,
                size: 48,
              ),
            ],
          ),
          const SizedBox(height: 4),
          RichText(
            text: const TextSpan(
              children: [
                TextSpan(
                  text: '45 000',
                  style: TextStyle(
                    color: Colors.white,
                    fontSize: 32,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                TextSpan(
                  text: ' FCFA',
                  style: TextStyle(color: Colors.white70, fontSize: 16),
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          Row(
            children: const [
              Expanded(
                child: FinanceInlineMetric(
                  label: 'En attente',
                  value: '18 200 F',
                ),
              ),
              SizedBox(width: 12),
              Expanded(
                child: FinanceInlineMetric(
                  label: 'CA ce mois',
                  value: '312 500 F',
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          FilledButton.icon(
            style: FilledButton.styleFrom(
              backgroundColor: Colors.white,
              foregroundColor: const Color(0xFF047857),
              minimumSize: const Size.fromHeight(48),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
            ),
            onPressed: onWithdraw,
            icon: const Icon(Icons.arrow_upward_rounded),
            label: const Text(
              "Retirer de l'argent",
              style: TextStyle(fontWeight: FontWeight.w900),
            ),
          ),
        ],
      ),
    );
  }
}

class FinanceInlineMetric extends StatelessWidget {
  const FinanceInlineMetric({
    super.key,
    required this.label,
    required this.value,
  });

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.16),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            style: const TextStyle(color: Colors.white70, fontSize: 12),
          ),
          const SizedBox(height: 6),
          Text(
            value,
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.w900,
              fontSize: 16,
            ),
          ),
        ],
      ),
    );
  }
}

class FinanceMiniCard extends StatelessWidget {
  const FinanceMiniCard({
    super.key,
    required this.label,
    required this.value,
    required this.color,
  });

  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      padding: const EdgeInsets.all(12),
      margin: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(color: _muted, fontSize: 12),
          ),
          const SizedBox(height: 6),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(color: color, fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class TransactionTile extends StatelessWidget {
  const TransactionTile(
    this.title,
    this.amount,
    this.date,
    this.color, {
    super.key,
    required this.incoming,
  });

  final String title;
  final String amount;
  final String date;
  final Color color;
  final bool incoming;

  @override
  Widget build(BuildContext context) {
    return HbaCard(
      child: Row(
        children: [
          IconBox(
            icon: incoming
                ? Icons.arrow_downward_rounded
                : Icons.arrow_upward_rounded,
            color: color,
            size: 40,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.w800),
                ),
                Text(date, style: const TextStyle(color: _muted)),
              ],
            ),
          ),
          Text(
            amount,
            style: TextStyle(color: color, fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

InputDecoration inputDecoration(String label) {
  return InputDecoration(
    labelText: label,
    filled: true,
    fillColor: Colors.white,
    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
    border: OutlineInputBorder(
      borderRadius: BorderRadius.circular(14),
      borderSide: const BorderSide(color: _border),
    ),
    enabledBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(14),
      borderSide: const BorderSide(color: _border),
    ),
    focusedBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(14),
      borderSide: const BorderSide(color: _green, width: 1.4),
    ),
  );
}

String sampleValue(String field) {
  final lower = field.toLowerCase();
  if (lower.contains('prix')) return '3500';
  if (lower.contains('nom')) return 'Samsung Galaxy A35';
  if (lower.contains('catégorie')) return 'Téléphones';
  if (lower.contains('temps')) return '25 min';
  if (lower.contains('adresse')) return 'Akpakpa, Cotonou';
  if (lower.contains('stock') || lower.contains('quantité')) return '12';
  if (lower.contains('garantie')) return '12 mois';
  if (lower.contains('allergènes')) return 'Arachide, gluten';
  if (lower.contains('extras')) return 'Alloco, sauce, boisson';
  if (lower.contains('ram')) return '8 Go';
  if (lower.contains('stockage')) return '128 Go';
  return '';
}

void pushPage(BuildContext context, Widget page) {
  Navigator.of(context).push(MaterialPageRoute(builder: (_) => page));
}

const _titleStyle = TextStyle(fontSize: 18, fontWeight: FontWeight.w900);
const _fieldLabelStyle = TextStyle(
  color: _muted,
  fontSize: 13,
  fontWeight: FontWeight.w800,
);
