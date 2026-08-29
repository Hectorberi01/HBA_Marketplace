import 'package:flutter/material.dart';

import '../../core/mock/driver_mock_data.dart';
import '../deliveries/deliveries_screen.dart';
import '../home/dashboard_screen.dart';
import '../notifications/notifications_screen.dart';
import '../profile/profile_screen.dart';
import '../wallet/wallet_screen.dart';

class DriverHomeScreen extends StatefulWidget {
  const DriverHomeScreen({super.key});

  @override
  State<DriverHomeScreen> createState() => _DriverHomeScreenState();
}

class _DriverHomeScreenState extends State<DriverHomeScreen> {
  int _selectedIndex = 0;
  bool _available = true;

  @override
  Widget build(BuildContext context) {
    final pages = [
      DashboardScreen(
        available: _available,
        activeDelivery: activeDelivery,
        proposedDeliveries: proposedDeliveries,
        onAvailabilityChanged: (value) => setState(() => _available = value),
      ),
      DeliveriesScreen(
        activeDelivery: activeDelivery,
        proposedDeliveries: proposedDeliveries,
        completedDeliveries: completedDeliveries,
      ),
      WalletScreen(entries: walletEntries),
      ProfileScreen(profile: driverProfile),
    ];

    return Scaffold(
      appBar: AppBar(
        title: const Text('HBA Driver'),
        actions: [
          IconButton(
            tooltip: 'Notifications',
            onPressed: () {
              Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => const NotificationsScreen(),
                ),
              );
            },
            icon: const Icon(Icons.notifications_none),
          ),
        ],
      ),
      body: SafeArea(child: pages[_selectedIndex]),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _selectedIndex,
        onDestinationSelected: (index) =>
            setState(() => _selectedIndex = index),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.dashboard_outlined),
            selectedIcon: Icon(Icons.dashboard),
            label: 'Accueil',
          ),
          NavigationDestination(
            icon: Icon(Icons.local_shipping_outlined),
            selectedIcon: Icon(Icons.local_shipping),
            label: 'Courses',
          ),
          NavigationDestination(
            icon: Icon(Icons.account_balance_wallet_outlined),
            selectedIcon: Icon(Icons.account_balance_wallet),
            label: 'Solde',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profil',
          ),
        ],
      ),
    );
  }
}
