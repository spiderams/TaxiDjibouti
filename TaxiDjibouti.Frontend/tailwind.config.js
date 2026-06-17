/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        taxi: {
          navy: '#132238',
          blue: '#263f5c',
          yellow: '#f7b733',
          sand: '#f5efe4',
        },
      },
      boxShadow: {
        soft: '0 24px 70px rgba(19, 34, 56, 0.11)',
      },
    },
  },
  plugins: [],
};
