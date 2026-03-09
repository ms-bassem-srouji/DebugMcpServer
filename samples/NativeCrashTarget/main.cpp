#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <vector>
#include <string>
#include <random>

#ifndef _WIN32
#include <unistd.h>
#include <sys/wait.h>
#endif

#ifdef _WIN32
#include <windows.h>
#include <dbghelp.h>
#pragma comment(lib, "dbghelp.lib")
#endif

// --- Application state for dump inspection ---

struct Item {
    char name[64];
    double price;
    int quantity;
};

struct Order {
    int id;
    char customer[128];
    double total;
    int itemCount;
    Item items[8];
};

static const char* CUSTOMER_NAMES[] = {
    "Alice Johnson", "Bob Smith", "Charlie Brown", "Diana Prince",
    "Eve Martinez", "Frank Castle", "Grace Hopper", "Hank Pym",
    "Iris West", "Jack Reacher", "Karen Page", "Leo Fitz"
};
static const int NUM_CUSTOMERS = 12;

static const char* ITEM_NAMES[] = {
    "Widget", "Gadget", "Thingamajig", "Doohickey", "Whatsit",
    "Gizmo", "Contraption", "Doodad", "Sprocket", "Flanget",
    "Cog", "Lever", "Pulley", "Gear", "Spring"
};
static const int NUM_ITEMS = 15;

// Global state visible in the dump
static constexpr int MAX_ORDERS = 10;
static Order g_orders[MAX_ORDERS];
static int g_orderCount = 0;
static const char* g_currentCustomer = nullptr;
static double g_totalRevenue = 0.0;
static int g_seed = 0;

void addOrder(int id, const char* customer, const Item* items, int itemCount) {
    Order& order = g_orders[g_orderCount];
    order.id = id;
    strncpy(order.customer, customer, sizeof(order.customer) - 1);
    order.customer[sizeof(order.customer) - 1] = '\0';
    order.total = 0.0;
    order.itemCount = itemCount;

    for (int i = 0; i < itemCount && i < 8; i++) {
        order.items[i] = items[i];
        order.total += items[i].price * items[i].quantity;
    }

    g_totalRevenue += order.total;
    g_currentCustomer = order.customer;
    g_orderCount++;
}

#ifdef _WIN32
static void writeMiniDump(const char* path) {
    HANDLE hFile = CreateFileA(path, GENERIC_WRITE, 0, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE) {
        fprintf(stderr, "Failed to create dump file: %s (error %lu)\n", path, GetLastError());
        return;
    }

    HANDLE hProcess = GetCurrentProcess();
    DWORD pid = GetCurrentProcessId();

    BOOL ok = MiniDumpWriteDump(hProcess, pid, hFile,
        MiniDumpWithFullMemory, nullptr, nullptr, nullptr);

    CloseHandle(hFile);

    if (ok) {
        // Get file size
        WIN32_FILE_ATTRIBUTE_DATA fad;
        GetFileAttributesExA(path, GetFileExInfoStandard, &fad);
        double sizeMb = (double)fad.nFileSizeLow / (1024.0 * 1024.0);
        printf("Dump written: %s (%.1f MB)\n", path, sizeMb);
    } else {
        fprintf(stderr, "MiniDumpWriteDump failed: 0x%08lX\n", GetLastError());
    }
}
#else
#include <signal.h>
static void writeMiniDump(const char* path) {
    // Fork a child, abort it to generate core dump, parent continues
    printf("Generating core dump via fork+abort...\n");
    fflush(stdout);

    pid_t child = fork();
    if (child == 0) {
        // Child: abort to generate core dump
        abort();
    } else if (child > 0) {
        // Parent: wait for child to dump
        int status;
        waitpid(child, &status, 0);

        // Find core file
        char corePath[256];
        snprintf(corePath, sizeof(corePath), "/tmp/core.%d", child);
        if (access(corePath, F_OK) == 0) {
            printf("Core dump written: %s\n", corePath);
        } else {
            printf("Core dump may be at a different location. Check /proc/sys/kernel/core_pattern\n");
            printf("Child PID was: %d, exit status: %d\n", child, status);
        }
    } else {
        printf("fork() failed\n");
    }
}
#endif

int main(int argc, char* argv[]) {
    printf("NativeCrashTarget PID: %d\n", 
#ifdef _WIN32
        (int)GetCurrentProcessId()
#else
        getpid()
#endif
    );

    // Seed random number generator
    std::mt19937 rng(static_cast<unsigned>(time(nullptr)));
    g_seed = static_cast<int>(rng());
    printf("Random seed: %d\n\n", g_seed);

    // Generate random number of orders (3-8)
    std::uniform_int_distribution<int> orderCountDist(3, 8);
    int numOrders = orderCountDist(rng);
    if (numOrders > MAX_ORDERS) numOrders = MAX_ORDERS;

    std::uniform_int_distribution<int> customerDist(0, NUM_CUSTOMERS - 1);
    std::uniform_int_distribution<int> itemCountDist(1, 6);
    std::uniform_int_distribution<int> itemNameDist(0, NUM_ITEMS - 1);
    std::uniform_real_distribution<double> priceDist(1.99, 999.99);
    std::uniform_int_distribution<int> qtyDist(1, 20);

    printf("=== Generating %d random orders ===\n\n", numOrders);

    for (int o = 0; o < numOrders; o++) {
        int orderId = 1000 + static_cast<int>(rng() % 9000);
        const char* customer = CUSTOMER_NAMES[customerDist(rng)];
        int numItems = itemCountDist(rng);

        Item items[8] = {};
        printf("Order #%d for %s (%d items):\n", orderId, customer, numItems);

        for (int i = 0; i < numItems; i++) {
            strncpy(items[i].name, ITEM_NAMES[itemNameDist(rng)], sizeof(items[i].name) - 1);
            items[i].price = static_cast<int>(priceDist(rng) * 100.0) / 100.0; // round to cents
            items[i].quantity = qtyDist(rng);
            printf("  - %s: $%.2f x %d = $%.2f\n", 
                items[i].name, items[i].price, items[i].quantity,
                items[i].price * items[i].quantity);
        }

        addOrder(orderId, customer, items, numItems);
        printf("  Subtotal: $%.2f\n\n", g_orders[g_orderCount - 1].total);
    }

    printf("=== Summary ===\n");
    printf("Orders: %d, Total Revenue: $%.2f, Last Customer: %s\n\n",
        g_orderCount, g_totalRevenue, g_currentCustomer ? g_currentCustomer : "(null)");

    // Local variables visible on the stack at dump time
    int activeOrderId = g_orders[g_orderCount - 1].id;
    double activeTotal = g_orders[g_orderCount - 1].total;
    const char* activeCustomer = g_orders[g_orderCount - 1].customer;

    std::vector<double> allPrices;
    for (int o = 0; o < g_orderCount; o++)
        for (int i = 0; i < g_orders[o].itemCount; i++)
            allPrices.push_back(g_orders[o].items[i].price);

    char statusBuf[256];
    snprintf(statusBuf, sizeof(statusBuf), "Processing %d orders, revenue $%.2f, seed %d",
        g_orderCount, g_totalRevenue, g_seed);
    std::string statusMessage = statusBuf;

    // Determine dump path or wait mode
    std::string dumpPath;
    if (argc > 1 && strcmp(argv[1], "--wait") == 0) {
        printf("Process is running. Capture a dump with:\n");
#ifdef _WIN32
        printf("  procdump -ma %d native_crash.dmp\n", (int)GetCurrentProcessId());
#else
        printf("  gcore %d\n", getpid());
#endif
        printf("\nPress Enter to exit...\n");
        getchar();
        return 0;
    }

#ifdef _WIN32
    // Build dump path next to executable
    char exePath[MAX_PATH];
    GetModuleFileNameA(nullptr, exePath, MAX_PATH);
    dumpPath = exePath;
    auto lastSlash = dumpPath.find_last_of("\\/");
    if (lastSlash != std::string::npos)
        dumpPath = dumpPath.substr(0, lastSlash + 1);
    char pidStr[32];
    snprintf(pidStr, sizeof(pidStr), "native_crash_%d.dmp", (int)GetCurrentProcessId());
    dumpPath += pidStr;
#else
    char pidStr[64];
    snprintf(pidStr, sizeof(pidStr), "native_crash_%d.core", getpid());
    dumpPath = pidStr;
#endif

    // Keep locals alive
    (void)activeOrderId;
    (void)activeTotal;
    (void)activeCustomer;
    (void)statusMessage;
    (void)allPrices;

    writeMiniDump(dumpPath.c_str());

    printf("\n=== Test native dump debugging with DebugMcpServer ===\n\n");
    printf("Load with cpp adapter (cpptools/OpenDebugAD7):\n");
    printf("  load_dump_file(dumpPath: \"%s\", adapter: \"cpp\")\n\n", dumpPath.c_str());
    printf("Then inspect:\n");
    printf("  get_callstack()              -> see main() frame\n");
    printf("  get_variables(frameId: 0)    -> g_orders, g_totalRevenue, prices\n");
    printf("  disassemble(memoryReference: \"...\")  -> assembly at current IP\n");
    printf("  list_threads()               -> all threads\n");

    return 0;
}
