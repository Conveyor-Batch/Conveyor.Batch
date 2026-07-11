import { defineConfig } from 'vitepress'

const GITHUB_URL = 'https://github.com/Conveyor-Batch/Conveyor.Batch'

export default defineConfig({
  title: 'Conveyor.Batch',
  description: 'Reliable batch processing for .NET 8+',
  base: '/Conveyor.Batch/',
  head: [['link', { rel: 'icon', href: '/Conveyor.Batch/icon.svg', type: 'image/svg+xml' }]],

  themeConfig: {
    logo: '/icon.svg',

    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'API', link: '/api/core' },
      { text: 'ADRs', link: '/adr/' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Guide',
          items: [
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Core Concepts', link: '/guide/core-concepts' },
            { text: 'Chunk Engine', link: '/guide/chunk-engine' },
            { text: 'Restartability', link: '/guide/restartability' },
            { text: 'Partitioning', link: '/guide/partitioning' },
            { text: 'Conditional Flow', link: '/guide/conditional-flow' },
            { text: 'Skip & Retry', link: '/guide/skip-retry' },
            { text: 'Dead-Lettering', link: '/guide/dead-lettering' },
            { text: 'Graceful Shutdown', link: '/guide/graceful-shutdown' },
            { text: 'Heartbeat', link: '/guide/heartbeat' },
            { text: 'Observability', link: '/guide/observability' },
            { text: 'Hosting', link: '/guide/hosting' },
          ],
        },
      ],
      '/api/': [
        {
          text: 'API Reference',
          items: [
            { text: 'Core', link: '/api/core' },
            { text: 'Repository', link: '/api/repository' },
            { text: 'IO', link: '/api/io' },
            { text: 'Dapper (planned)', link: '/api/dapper' },
            { text: 'Testing', link: '/api/testing' },
          ],
        },
      ],
      '/adr/': [
        {
          text: 'Architecture Decision Records',
          items: [
            { text: 'Overview', link: '/adr/' },
            { text: 'ADR-001: Async Enumerable Reader', link: '/adr/001' },
            { text: 'ADR-002: EF Core Job Repository', link: '/adr/002' },
            { text: 'ADR-003: Polly Adapter Retry', link: '/adr/003' },
            { text: 'ADR-004: Channels Chunk Transport', link: '/adr/004' },
          ],
        },
      ],
    },

    socialLinks: [{ icon: 'github', link: GITHUB_URL }],

    search: {
      provider: 'local',
    },
  },
})
