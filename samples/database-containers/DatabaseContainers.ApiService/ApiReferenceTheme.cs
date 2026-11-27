namespace DatabaseContainers.ApiService;

/// <summary>
/// Custom CSS that gives the Scalar API reference a distinct Aspire violet identity in both color modes:
/// a deep indigo canvas in dark mode, a soft lavender canvas in light mode, a violet accent, and high-contrast typography.
/// </summary>
internal static class ApiReferenceTheme
{
    public const string Css =
        """
        /* Aspire violet theme for the Database Containers API reference. */
        :root {
            --scalar-font: "Inter", system-ui, -apple-system, "Segoe UI", sans-serif;
            --scalar-font-code: "JetBrains Mono", "Cascadia Code", ui-monospace, monospace;
            --scalar-radius: 10px;
            --scalar-radius-lg: 14px;
            --scalar-radius-xl: 18px;
        }

        .light-mode {
            --scalar-background-1: #ffffff;
            --scalar-background-2: #f7f5fe;
            --scalar-background-3: #ece8fb;
            --scalar-background-accent: rgba(109, 63, 214, 0.10);

            --scalar-color-1: #1a1530;
            --scalar-color-2: #4a4268;
            --scalar-color-3: #7a7298;
            --scalar-color-accent: #6d3fd6;

            --scalar-border-color: rgba(109, 63, 214, 0.16);

            --scalar-button-1: #6d3fd6;
            --scalar-button-1-hover: #5d31c4;
            --scalar-button-1-color: #ffffff;

            --scalar-color-green: #15803d;
            --scalar-color-blue: #2563eb;
            --scalar-color-red: #e11d48;
            --scalar-color-yellow: #b45309;
            --scalar-color-orange: #c2410c;
            --scalar-color-purple: #7c3aed;
        }

        .light-mode .sidebar,
        .light-mode {
            --scalar-sidebar-background-1: #faf8ff;
            --scalar-sidebar-color-1: #1a1530;
            --scalar-sidebar-color-2: #5a5278;
            --scalar-sidebar-border-color: rgba(109, 63, 214, 0.12);
            --scalar-sidebar-item-hover-background: rgba(109, 63, 214, 0.08);
            --scalar-sidebar-item-hover-color: #1a1530;
            --scalar-sidebar-item-active-background: rgba(109, 63, 214, 0.14);
            --scalar-sidebar-color-active: #5b2bc4;
            --scalar-sidebar-search-background: #ffffff;
            --scalar-sidebar-search-border-color: rgba(109, 63, 214, 0.20);
            --scalar-sidebar-search-color: #5a5278;
        }

        /* Soft violet glow behind the reference content. */
        .light-mode .references-rendered {
            background-color: var(--scalar-background-1);
            background-image:
                radial-gradient(900px 520px at 92% -4%, rgba(139, 92, 246, 0.12), transparent 60%),
                radial-gradient(760px 480px at 4% 102%, rgba(91, 99, 245, 0.08), transparent 55%);
            background-attachment: fixed, fixed;
        }

        /* Brand the API title with a violet gradient. */
        .light-mode .section-header.tight .section-header-label {
            background: linear-gradient(92deg, #1a1530 0%, #5b2bc4 60%, #8b5cf6 100%);
            -webkit-background-clip: text;
            background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .dark-mode {
            --scalar-background-1: #0e0c1a;
            --scalar-background-2: #161230;
            --scalar-background-3: #221c44;
            --scalar-background-accent: rgba(139, 92, 246, 0.22);

            --scalar-color-1: #f4f2fb;
            --scalar-color-2: #c9c3e8;
            --scalar-color-3: #978fbe;
            --scalar-color-accent: #b79dff;

            --scalar-border-color: rgba(167, 139, 250, 0.20);

            --scalar-button-1: #6d3fd6;
            --scalar-button-1-hover: #5d31c4;
            --scalar-button-1-color: #ffffff;

            --scalar-color-green: #4ade80;
            --scalar-color-blue: #7ca8ff;
            --scalar-color-red: #fb7185;
            --scalar-color-yellow: #fbbf24;
            --scalar-color-orange: #fb923c;
            --scalar-color-purple: #c4b5fd;

            --scalar-scrollbar-color: rgba(167, 139, 250, 0.28);
            --scalar-scrollbar-color-active: rgba(167, 139, 250, 0.5);
        }

        .dark-mode .sidebar,
        .dark-mode {
            --scalar-sidebar-background-1: #110d24;
            --scalar-sidebar-color-1: #f1eefb;
            --scalar-sidebar-color-2: #b4abd9;
            --scalar-sidebar-border-color: rgba(167, 139, 250, 0.16);
            --scalar-sidebar-item-hover-background: rgba(139, 92, 246, 0.16);
            --scalar-sidebar-item-hover-color: #ffffff;
            --scalar-sidebar-item-active-background: rgba(139, 92, 246, 0.30);
            --scalar-sidebar-color-active: #d6c8ff;
            --scalar-sidebar-search-background: #1a1536;
            --scalar-sidebar-search-border-color: rgba(167, 139, 250, 0.24);
            --scalar-sidebar-search-color: #c9c3e8;
        }

        /* Premium violet glow behind the reference content. */
        .dark-mode .references-rendered {
            background-color: var(--scalar-background-1);
            background-image:
                radial-gradient(900px 520px at 92% -4%, rgba(139, 92, 246, 0.28), transparent 60%),
                radial-gradient(760px 480px at 4% 102%, rgba(91, 99, 245, 0.18), transparent 55%);
            background-attachment: fixed, fixed;
        }

        .dark-mode .scalar-app,
        .dark-mode .scalar-api-reference {
            background-color: var(--scalar-background-1);
        }

        /* Tint Scalar's decorative flare to the Aspire violet. */
        .dark-mode .section-flare {
            background: radial-gradient(620px 320px at 80% 0%, rgba(167, 139, 250, 0.24), transparent 70%);
        }

        /* Brand the API title with a violet gradient. */
        .dark-mode .section-header.tight .section-header-label {
            background: linear-gradient(92deg, #ffffff 0%, #d6c8ff 55%, #a78bfa 100%);
            -webkit-background-clip: text;
            background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        /* Make the HTTP method badge feel crisp on the dark canvas. */
        .dark-mode .endpoint .method,
        .dark-mode .scalar-api-reference .http-method {
            letter-spacing: 0.04em;
            font-weight: 600;
        }
        """;
}
