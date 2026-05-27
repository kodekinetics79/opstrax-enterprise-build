import dotenv from "dotenv";
import { app } from "./app";

dotenv.config();

const PORT = process.env.PORT || 11000;

app.listen(PORT, () => {
  console.log(`Fleet backend running on http://localhost:${PORT}`);
});
