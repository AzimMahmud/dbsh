import { readFileSync } from 'fs'
import { resolve } from 'path'
import { defineConfig } from 'vitepress'

const csproj = readFileSync(resolve(__dirname, '../../src/dbsh.CLI/dbsh.CLI.csproj'), 'utf-8')
const version = csproj.match(/<Version>(.*?)<\/Version>/)?.[1] || '0.0.0'

export default defineConfig({
  title: 'dbsh',
  description: 'Database migrations that ship. A Flyway-style migration tool for PostgreSQL, SQL Server, MySQL, SQLite, Oracle, CockroachDB, YugabyteDB, and Aurora. Beautiful CLI, zero magic, production-tested patterns.',

  base: '/',

  head: [
    ['link', { rel: 'icon', type: 'image/png', href: '/icon.png' }]
  ],

  themeConfig: {
    logo: '/icon.png',
    siteTitle: 'dbsh',

    nav: [
      { text: 'Guide', link: '/guide/installation' },
      { text: 'Commands', link: '/commands/new' },
      { text: 'Reference', link: '/reference/global-options' },
      {
        text: `v${version}`,
        items: [
          { text: 'Changelog', link: 'https://github.com/AzimMahmud/dbsh/blob/main/CHANGELOG.md' },
          { text: 'GitHub', link: 'https://github.com/AzimMahmud/dbsh' }
        ]
      }
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Getting Started',
          items: [
            { text: 'Installation', link: '/guide/installation' },
            { text: 'Quick Start', link: '/guide/quick-start' },
            { text: 'Configuration', link: '/guide/configuration' },
            { text: 'Multi-Database Setup', link: '/guide/multi-database' }
          ]
        }
      ],

      '/commands/': [
        {
          text: 'Setup',
          items: [
            { text: 'new', link: '/commands/new' },
            { text: 'create', link: '/commands/create' },
            { text: 'init', link: '/commands/init' }
          ]
        },
        {
          text: 'Validation',
          items: [
            { text: 'validate', link: '/commands/validate' }
          ]
        },
        {
          text: 'Inspection',
          items: [
            { text: 'plan', link: '/commands/plan' },
            { text: 'status', link: '/commands/status' },
            { text: 'history', link: '/commands/history' },
            { text: 'info', link: '/commands/info' }
          ]
        },
        {
          text: 'Execution',
          items: [
            { text: 'migrate', link: '/commands/migrate' },
            { text: 'rollback', link: '/commands/rollback' },
            { text: 'repair', link: '/commands/repair' }
          ]
        }
      ],

      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Global Options', link: '/reference/global-options' },
            { text: 'Script Conventions', link: '/reference/script-conventions' },
            { text: 'Tracking Tables', link: '/reference/tracking-tables' },
            { text: 'Architecture', link: '/reference/architecture' },
            { text: 'CI/CD Integration', link: '/reference/ci-cd' }
          ]
        }
      ]
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/AzimMahmud/dbsh' }
    ],

    editLink: {
      pattern: 'https://github.com/AzimMahmud/dbsh/edit/main/docs/:path'
    },

    search: {
      provider: 'local'
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright 2025-present Azim Mahmud'
    }
  }
})
